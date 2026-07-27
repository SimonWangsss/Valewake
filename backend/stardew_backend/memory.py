import json
import re
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
from threading import RLock
from typing import Any, Dict, List
from uuid import uuid4

from stardew_backend.retrieval import score_text


class MemoryStore:
    def __init__(self, path: Path, max_episodes_per_session: int = 80):
        self.path = path
        self.checkpoint_path = path.with_name(f"{path.stem}.committed{path.suffix}")
        self.max_episodes_per_session = max(20, max_episodes_per_session)
        self._lock = RLock()
        self.data = self._load_path(self.path)
        if not self.checkpoint_path.exists():
            self._write(self.checkpoint_path, self.data)

    def _load_path(self, path: Path) -> Dict[str, Any]:
        empty = {
            "schema_version": 3,
            "memories": [],
            "episodes": [],
            "consolidation": {},
        }
        if not path.exists():
            return empty
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
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
            item.setdefault(
                "canonical_key",
                canonical_memory_key(
                    str(item.get("text", "")),
                    str(item.get("kind", "preference")),
                ),
            )
            item.setdefault("polarity", memory_polarity(str(item.get("text", ""))))
            item.setdefault("status", "active")
            item.setdefault("supersedes", "")
        consolidation = (
            data.get("consolidation")
            if isinstance(data.get("consolidation"), dict)
            else {}
        )
        return {
            "schema_version": 3,
            "memories": memories,
            "episodes": episodes,
            "consolidation": consolidation,
        }

    def save(self) -> None:
        with self._lock:
            self._write(self.path, self.data)

    @staticmethod
    def _write(path: Path, data: Dict[str, Any]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary_path = path.with_suffix(path.suffix + ".tmp")
        temporary_path.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        temporary_path.replace(path)

    def commit_session(self, session_prefix: str) -> None:
        with self._lock:
            committed = self._load_path(self.checkpoint_path)
            self._replace_session_data(committed, self.data, session_prefix)
            self._write(self.checkpoint_path, committed)
            self._write(self.path, self.data)

    def rollback_session(self, session_prefix: str) -> None:
        with self._lock:
            committed = self._load_path(self.checkpoint_path)
            self._replace_session_data(self.data, committed, session_prefix)
            self._write(self.path, self.data)

    @staticmethod
    def _replace_session_data(
        destination: Dict[str, Any],
        source: Dict[str, Any],
        session_prefix: str,
    ) -> None:
        def matches(item: Dict[str, Any]) -> bool:
            return str(item.get("session_id", "")).startswith(session_prefix)

        for collection in ("memories", "episodes"):
            retained = [
                item for item in destination[collection]
                if not matches(item)
            ]
            retained.extend(
                deepcopy(item) for item in source[collection] if matches(item)
            )
            destination[collection] = retained

        destination_consolidation = destination["consolidation"]
        for key in list(destination_consolidation):
            if str(key).startswith(session_prefix):
                del destination_consolidation[key]
        for key, value in source["consolidation"].items():
            if str(key).startswith(session_prefix):
                destination_consolidation[key] = deepcopy(value)

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
        canonical_key: str = "",
    ) -> bool:
        text = clean_text(text)[:180]
        if not text:
            return False
        normalized = normalize_text(text)
        canonical_key = canonical_key or canonical_memory_key(text, kind)
        polarity = memory_polarity(text)
        now = utc_now()
        superseded_id = ""
        with self._lock:
            for item in self.data["memories"]:
                if item.get("session_id") != session_id:
                    continue
                if item.get("status", "active") != "active":
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

            if canonical_key:
                for item in self.data["memories"]:
                    if item.get("session_id") != session_id:
                        continue
                    if item.get("status", "active") != "active":
                        continue
                    if item.get("canonical_key") != canonical_key:
                        continue
                    prior_polarity = str(item.get("polarity", "unknown"))
                    if (
                        polarity != "unknown"
                        and prior_polarity != "unknown"
                        and polarity != prior_polarity
                    ):
                        item["status"] = "superseded"
                        item["updated_at"] = now
                        superseded_id = str(item.get("id", ""))
                        continue
                    if score_text(text, str(item.get("text", ""))) >= 0.78:
                        item["reinforcement_count"] = int(
                            item.get("reinforcement_count", 1)
                        ) + 1
                        item["importance"] = max(
                            int(item.get("importance", 1)),
                            max(1, min(3, importance)),
                        )
                        item["confidence"] = max(
                            float(item.get("confidence", 0.0)),
                            max(0.0, min(1.0, confidence)),
                        )
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
                "canonical_key": canonical_key,
                "polarity": polarity,
                "status": "active",
                "supersedes": superseded_id,
            })
            self.save()
        return True

    def search(self, query: str, session_id: str, top_k: int, game_day: int | None = None) -> List[str]:
        scored: list[tuple[float, dict[str, Any]]] = []
        with self._lock:
            for item in self.data["memories"]:
                if item.get("session_id") != session_id:
                    continue
                if item.get("status", "active") != "active":
                    continue
                text = str(item.get("text", ""))
                searchable = " ".join([
                    text,
                    str(item.get("evidence", "")),
                    str(item.get("kind", "")),
                    str(item.get("canonical_key", "")),
                ])
                relevance = score_text(query, searchable)
                if relevance <= 0:
                    continue
                importance = max(1, min(3, int(item.get("importance", 1) or 1)))
                reinforcement = min(5, int(item.get("reinforcement_count", 1) or 1))
                recency = self._recency_score(game_day, item.get("game_day"))
                kind_score = memory_kind_query_score(
                    query,
                    str(item.get("kind", "")),
                )
                score = (
                    relevance * 0.69
                    + importance * 0.06
                    + reinforcement * 0.025
                    + recency * 0.08
                    + kind_score
                )
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

    def durable_profile(self, session_id: str, limit: int = 6) -> list[str]:
        with self._lock:
            memories = [
                item
                for item in self.data["memories"]
                if item.get("session_id") == session_id
                and item.get("status", "active") == "active"
                and int(item.get("importance", 1) or 1) >= 2
            ]
            memories.sort(
                key=lambda item: (
                    int(item.get("importance", 1) or 1),
                    int(item.get("reinforcement_count", 1) or 1),
                    str(item.get("updated_at", "")),
                ),
                reverse=True,
            )
            return [
                str(item.get("text", ""))
                for item in memories[:max(0, limit)]
            ]

    def social_context(
        self,
        player_input: str,
        session_id: str,
        game_day: int | None,
        relationship: dict[str, Any],
    ) -> dict[str, Any]:
        episodes = self.recent_episodes(session_id, 30)
        similarities = [score_text(player_input, str(item.get("player_input", ""))) for item in episodes]
        same_day_similarities = [
            score
            for score, item in zip(similarities, episodes)
            if game_day is None or item.get("game_day") == game_day
        ]
        repeat_count = sum(1 for score in same_day_similarities if score >= 0.72)
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
            self.data["consolidation"][session_id] = {
                "episode_count": len(session_episodes),
                "active_semantic_memories": sum(
                    1
                    for item in self.data["memories"]
                    if item.get("session_id") == session_id
                    and item.get("status", "active") == "active"
                ),
                "last_game_day": game_day,
                "updated_at": utc_now(),
            }
            self.save()
        return episode_id


def extract_memories(player_input: str) -> list[tuple[str, str, int, str]]:
    text = clean_text(player_input)
    if not text or is_unanchored_question(text):
        return []
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
    if memories:
        return deduplicate_extracted_memories(memories)

    chinese_patterns = [
        (
            r"(?:我叫|请叫我|叫我)[，,：: ]*([\u4e00-\u9fffA-Za-z0-9_]{1,24})",
            "玩家希望被称为{0}。",
            "profile",
            3,
        ),
        (
            r"我(?:最|更)?(?:喜欢|偏爱)(.{1,48})",
            "玩家喜欢{0}。",
            "preference",
            2,
        ),
        (
            r"我(?:不喜欢|讨厌)(.{1,48})",
            "玩家不喜欢{0}。",
            "preference",
            2,
        ),
        (
            r"我(?:通常|习惯)(.{2,48})",
            "玩家通常{0}。",
            "profile",
            2,
        ),
        (
            r"我(?:觉得|认为)(.{2,60})",
            "玩家认为{0}。",
            "opinion",
            2,
        ),
        (
            r"我(?:的长期目标是|打算长期|一直想)(.{2,60})",
            "玩家的长期目标是{0}。",
            "goal",
            2,
        ),
    ]
    for pattern, template, kind, importance in chinese_patterns:
        match = re.search(pattern, text)
        if not match:
            continue
        value = match.group(1).strip(" .,;:!?，。；：！？")
        if not value or contains_question_marker(value):
            continue
        memories.append(
            (template.format(value), kind, importance, match.group(0))
        )
    return deduplicate_extracted_memories(memories)


def deduplicate_extracted_memories(
    memories: list[tuple[str, str, int, str]],
) -> list[tuple[str, str, int, str]]:
    deduplicated: list[tuple[str, str, int, str]] = []
    seen: set[str] = set()
    for memory in memories:
        normalized = normalize_text(memory[0])
        if normalized in seen:
            continue
        seen.add(normalized)
        deduplicated.append(memory)
    return deduplicated


def contains_question_marker(text: str) -> bool:
    normalized = (text or "").strip().lower()
    return (
        "?" in normalized
        or "？" in normalized
        or normalized.startswith((
            "who ", "what ", "when ", "where ", "why ", "how ",
            "do you ", "are you ", "can you ", "would you ",
            "谁", "什么", "什么时候", "哪里", "哪儿", "为什么", "怎么",
            "你会", "你能", "你喜欢", "你觉得",
        ))
    )


def is_unanchored_question(text: str) -> bool:
    normalized = (text or "").strip().lower()
    explicit_memory_request = any(marker in normalized for marker in (
        "remember that", "remember,", "请记住", "记住：", "记住,",
    ))
    return contains_question_marker(normalized) and not explicit_memory_request


def is_durable_player_evidence(text: str) -> bool:
    normalized = (text or "").strip().lower()
    if not normalized or is_unanchored_question(normalized):
        return False
    markers = (
        "i am ", "i'm ", "my ", "i like", "i love", "i prefer",
        "i dislike", "i hate", "i usually", "i always", "i never",
        "i promise", "remember that", "call me", "my name is",
        "我叫", "我是", "我的", "我喜欢", "我最喜欢", "我更喜欢",
        "我偏爱", "我不喜欢", "我讨厌", "我通常", "我习惯",
        "我觉得", "我认为", "我一直", "我从不", "我答应", "请记住", "记住",
    )
    return any(marker in normalized for marker in markers)


def canonical_memory_key(text: str, kind: str) -> str:
    normalized = normalize_text(text)
    topic_markers: dict[str, tuple[str, ...]] = {
        "name": ("called", "name", "称为", "名字", "我叫"),
        "fishing": ("fishing", "fish", "钓鱼"),
        "mining": ("mining", "mine", "矿洞", "采矿"),
        "farming": ("farming", "crop", "种田", "种植", "作物"),
        "rain": ("rain", "雨天", "下雨"),
        "late_night": ("stay out late", "late", "pass out", "熬夜", "晚睡"),
        "coffee": ("coffee", "咖啡"),
        "hot_cocoa": ("hot cocoa", "可可"),
        "money": ("money", "gold", "profit", "赚钱", "金币"),
        "adventure": ("adventure", "explore", "冒险", "探索"),
    }
    for topic, markers in topic_markers.items():
        if any(normalize_text(marker) in normalized for marker in markers):
            return f"{kind}:{topic}"
    return ""


def memory_polarity(text: str) -> str:
    normalized = normalize_text(text)
    negative_markers = (
        "do not like", "don't like", "dislike", "hate", "never",
        "prefers not", "不喜欢", "讨厌", "不要", "从不",
    )
    positive_markers = (
        "like", "love", "prefer", "enjoy", "喜欢", "偏爱", "爱好",
    )
    if any(normalize_text(marker) in normalized for marker in negative_markers):
        return "negative"
    if any(normalize_text(marker) in normalized for marker in positive_markers):
        return "positive"
    return "unknown"


def memory_kind_query_score(query: str, kind: str) -> float:
    normalized = normalize_text(query)
    markers: dict[str, tuple[str, ...]] = {
        "profile": ("who am i", "my name", "about me", "我是谁", "我的名字"),
        "preference": (
            "what do i like", "prefer", "favorite", "我喜欢", "偏好", "最喜欢",
        ),
        "promise": ("promise", "agreed", "答应", "约定"),
        "opinion": ("think about", "opinion", "态度", "怎么看"),
        "goal": ("goal", "plan", "目标", "打算"),
        "note": ("remember", "记得", "记住"),
    }
    return 0.12 if any(
        normalize_text(marker) in normalized
        for marker in markers.get(kind, ())
    ) else 0.0


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
