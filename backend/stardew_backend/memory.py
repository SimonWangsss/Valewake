import json
import re
from datetime import datetime, timezone
from pathlib import Path
from threading import RLock
from typing import Any, Dict, List
from uuid import uuid4

from stardew_backend.retrieval import score_text


class MemoryStore:
    def __init__(self, path: Path, max_episodes_per_session: int = 80):
        self.path = path
        self.max_episodes_per_session = max(20, max_episodes_per_session)
        self._lock = RLock()
        self.data = self._load()

    def _load(self) -> Dict[str, Any]:
        empty = {"schema_version": 2, "memories": [], "episodes": []}
        if not self.path.exists():
            return empty
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return empty
        if not isinstance(data, dict):
            return empty
        memories = data.get("memories") if isinstance(data.get("memories"), list) else []
        episodes = data.get("episodes") if isinstance(data.get("episodes"), list) else []
        for item in memories:
            if not isinstance(item, dict):
                continue
            item.setdefault("reinforcement_count", 1)
            item.setdefault("access_count", 0)
            item.setdefault("confidence", 1.0)
            item.setdefault("evidence", "")
            item.setdefault("source", "legacy")
            item.setdefault("updated_at", item.get("created_at", utc_now()))
            item.setdefault("last_accessed_at", "")
            item.setdefault("game_day", None)
        return {"schema_version": 2, "memories": memories, "episodes": episodes}

    def save(self) -> None:
        with self._lock:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            temporary_path = self.path.with_suffix(self.path.suffix + ".tmp")
            temporary_path.write_text(
                json.dumps(self.data, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
            temporary_path.replace(self.path)

    def add(
        self,
        text: str,
        session_id: str,
        kind: str = "preference",
        importance: int = 1,
        evidence: str = "",
        confidence: float = 1.0,
        game_day: int | None = None,
        source: str = "rule",
    ) -> bool:
        text = clean_text(text)[:180]
        if not text:
            return False
        normalized = normalize_text(text)
        now = utc_now()
        with self._lock:
            for item in self.data["memories"]:
                if item.get("session_id") != session_id:
                    continue
                if normalize_text(str(item.get("text", ""))) != normalized:
                    continue
                item["reinforcement_count"] = int(item.get("reinforcement_count", 1)) + 1
                item["importance"] = max(int(item.get("importance", 1)), max(1, min(3, importance)))
                item["confidence"] = max(float(item.get("confidence", 0.0)), max(0.0, min(1.0, confidence)))
                item["updated_at"] = now
                if evidence:
                    item["evidence"] = clean_text(evidence)[:160]
                if game_day is not None:
                    item["game_day"] = game_day
                self.save()
                return True

            self.data["memories"].append({
                "id": uuid4().hex,
                "session_id": session_id,
                "kind": kind,
                "text": text,
                "importance": max(1, min(3, importance)),
                "confidence": max(0.0, min(1.0, confidence)),
                "evidence": clean_text(evidence)[:160],
                "source": source,
                "reinforcement_count": 1,
                "access_count": 0,
                "created_at": now,
                "updated_at": now,
                "last_accessed_at": "",
                "game_day": game_day,
            })
            self.save()
        return True

    def search(self, query: str, session_id: str, top_k: int, game_day: int | None = None) -> List[str]:
        scored: list[tuple[float, dict[str, Any]]] = []
        with self._lock:
            for item in self.data["memories"]:
                if item.get("session_id") != session_id:
                    continue
                text = str(item.get("text", ""))
                relevance = score_text(query, text)
                if relevance <= 0:
                    continue
                importance = max(1, min(3, int(item.get("importance", 1) or 1)))
                reinforcement = min(5, int(item.get("reinforcement_count", 1) or 1))
                recency = self._recency_score(game_day, item.get("game_day"))
                score = relevance * 0.72 + importance * 0.06 + reinforcement * 0.025 + recency * 0.08
                scored.append((score, item))
            scored.sort(key=lambda pair: pair[0], reverse=True)
            selected = [item for _, item in scored[:max(0, top_k)]]
            if selected:
                now = utc_now()
                for item in selected:
                    item["access_count"] = int(item.get("access_count", 0)) + 1
                    item["last_accessed_at"] = now
                self.save()
            return [str(item.get("text", "")) for item in selected]

    @staticmethod
    def _recency_score(current_day: int | None, memory_day: Any) -> float:
        if current_day is None or not isinstance(memory_day, int):
            return 0.0
        age = max(0, current_day - memory_day)
        return 1.0 / (1.0 + age / 14.0)

    def recent_episodes(self, session_id: str, limit: int = 6) -> list[dict[str, Any]]:
        with self._lock:
            episodes = [item for item in self.data["episodes"] if item.get("session_id") == session_id]
            return [dict(item) for item in episodes[-max(0, limit):]]

    def social_context(
        self,
        player_input: str,
        session_id: str,
        game_day: int | None,
        relationship: dict[str, Any],
    ) -> dict[str, Any]:
        episodes = self.recent_episodes(session_id, 30)
        similarities = [score_text(player_input, str(item.get("player_input", ""))) for item in episodes]
        repeat_count = sum(1 for score in similarities if score >= 0.72)
        maximum_similarity = max(similarities, default=0.0)
        previous_days = [item.get("game_day") for item in episodes if isinstance(item.get("game_day"), int)]
        days_since = None
        if game_day is not None and previous_days:
            days_since = max(0, game_day - max(previous_days))

        hearts = int(relationship.get("hearts", 0) or 0)
        status = str(relationship.get("status", "acquaintance") or "acquaintance").lower()
        intimacy_level = detect_intimacy_level(player_input)
        romantic_relationship = status in {"dating", "engaged", "married"}
        intimacy_mismatch = (
            intimacy_level == "high" and not romantic_relationship
        ) or (
            intimacy_level == "medium" and not romantic_relationship and hearts < 6
        )
        boundary_pressure = repeat_count >= 2

        return {
            "prior_interaction_count": len(episodes),
            "days_since_last_interaction": days_since,
            "semantic_repeat_count": repeat_count,
            "maximum_prior_similarity": round(maximum_similarity, 3),
            "intimacy_level": intimacy_level,
            "intimacy_mismatch": intimacy_mismatch,
            "boundary_pressure": boundary_pressure,
            "relationship_hearts": hearts,
            "relationship_status": status,
            "recent_emotions": [str(item.get("emotion", "neutral")) for item in episodes[-3:]],
        }

    def record_episode(
        self,
        session_id: str,
        player_input: str,
        reply: str,
        emotion: str,
        game_day: int | None,
        policy: dict[str, Any],
    ) -> str:
        episode_id = uuid4().hex
        with self._lock:
            self.data["episodes"].append({
                "id": episode_id,
                "session_id": session_id,
                "player_input": clean_text(player_input)[:600],
                "reply": clean_text(reply)[:800],
                "emotion": emotion,
                "game_day": game_day,
                "policy_stance": policy.get("response_stance", "respond_naturally"),
                "created_at": utc_now(),
            })
            session_episodes = [
                item for item in self.data["episodes"] if item.get("session_id") == session_id
            ]
            overflow = len(session_episodes) - self.max_episodes_per_session
            if overflow > 0:
                remove_ids = {item.get("id") for item in session_episodes[:overflow]}
                self.data["episodes"] = [
                    item for item in self.data["episodes"] if item.get("id") not in remove_ids
                ]
            self.save()
        return episode_id


def extract_memories(player_input: str) -> list[tuple[str, str, int, str]]:
    text = clean_text(player_input)
    memories: list[tuple[str, str, int, str]] = []
    patterns = [
        (r"(?:my name is|call me)\s+([A-Za-z0-9_]{1,24})", "Player prefers to be called {0}.", "profile", 3),
        (r"(?:i want|i'm trying|i am trying).{0,20}(?:money|gold|profit)", "Player is currently prioritizing money/profit.", "preference", 2),
        (r"(?:don't|do not).{0,12}(?:stay up|late|pass out)", "Player prefers not to stay out late.", "preference", 2),
        (r"(?:i like|i prefer).{0,16}(?:mining|mine)", "Player enjoys or prefers mining.", "preference", 2),
        (r"(?:i like|i prefer).{0,16}(?:fishing|fish)", "Player enjoys or prefers fishing.", "preference", 2),
        (r"(?:i like|i prefer).{0,16}(?:farming|crops)", "Player enjoys or prefers farming/crops.", "preference", 2),
        (r"(?:remember that|remember,)\s+(.{4,120})", "Player asked to remember: {0}", "note", 2),
        (r"(?:请记住|记住)[，,:： ]*(.{2,80})", "玩家希望记住：{0}", "note", 2),
    ]
    lowered = text.lower()
    for pattern, template, kind, importance in patterns:
        match = re.search(pattern, lowered, flags=re.IGNORECASE)
        if match:
            value = match.group(1).strip(" .,;:!?，。；：！？") if match.groups() else ""
            memories.append((template.format(value), kind, importance, match.group(0)))
    return memories


def detect_intimacy_level(text: str) -> str:
    normalized = normalize_text(text)
    high_markers = [
        "do you love me", "are you in love with me", "marry me", "be my girlfriend", "be my boyfriend",
        "你爱我吗", "你爱不爱我", "嫁给我", "娶我", "做我女朋友", "做我男朋友",
    ]
    medium_markers = ["do you like me", "date me", "你喜欢我吗", "约会", "亲我", "kiss me"]
    if any(normalize_text(marker) in normalized for marker in high_markers):
        return "high"
    if any(normalize_text(marker) in normalized for marker in medium_markers):
        return "medium"
    return "ordinary"


def normalize_text(text: str) -> str:
    return re.sub(r"[^a-z0-9\u4e00-\u9fff]+", " ", (text or "").lower()).strip()


def clean_text(text: str) -> str:
    return (text or "").strip()


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()
