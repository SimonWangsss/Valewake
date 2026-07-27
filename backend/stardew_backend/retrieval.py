import json
import math
import re
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List


TOPIC_MARKERS: dict[str, tuple[str, ...]] = {
    "identity": (
        "who are you", "where do you live", "your parents", "your family",
        "你是谁", "你住哪", "你家", "父母", "爸爸", "妈妈", "皮埃尔", "卡洛琳",
    ),
    "interests": (
        "what do you like", "your hobby", "interests", "adventure", "sword",
        "你喜欢什么", "爱好", "冒险", "剑", "探险",
    ),
    "mines": (
        "mine", "mines", "mining", "cave", "ore", "monster",
        "矿洞", "矿井", "采矿", "矿石", "洞穴", "怪物",
    ),
    "games": ("video game", "games", "gaming", "游戏", "电子游戏"),
    "music": ("music", "drum", "band", "音乐", "鼓", "乐队"),
    "occult": (
        "occult", "spirit", "ghost", "mysterious", "supernatural",
        "神秘学", "灵异", "幽灵", "鬼", "超自然", "神秘",
    ),
    "relationships": (
        "sam", "sebastian", "friend", "friends",
        "山姆", "塞巴斯蒂安", "朋友",
    ),
    "relationship": (
        "heart", "hearts", "dating", "romance", "marry", "married", "love me",
        "好感", "几颗心", "恋爱", "约会", "结婚", "爱我", "喜欢我",
    ),
    "repeated_topic": (
        "asked before", "again", "repeat", "repeating",
        "问过", "又问", "重复", "反复问", "同一个问题", "再问一遍",
    ),
    "conflict": (
        "disagree", "argument", "apolog", "sorry",
        "不同意", "同意我的", "任何意见", "夸你", "争论", "道歉", "对不起", "吵架",
    ),
    "reconnection": (
        "long time", "been a while", "miss me",
        "好久不见", "很久没聊", "很多天没聊", "对话从没中断", "想我吗", "几天没见",
    ),
    "weather": ("rain", "raining", "weather", "下雨", "雨天", "天气"),
    "farm_advice": (
        "what should i do", "any advice", "suggestion", "recommend",
        "今天做什么", "有什么建议", "给点建议", "不知道做什么",
    ),
    "late_night": (
        "late night", "midnight", "sleep", "too late",
        "深夜", "半夜", "太晚", "晚上十一", "十一点", "11点", "睡觉", "熬夜",
    ),
    "low_energy": (
        "energy", "stamina", "exhausted", "tired",
        "体力", "精力", "没力气", "累了", "疲惫",
    ),
    "inventory": (
        "inventory", "backpack", "full bag", "empty slot",
        "背包", "物品栏", "装满", "空位",
    ),
    "perception": (
        "can you see", "do you know my", "how much money", "my whole farm",
        "你看得到", "你知道我", "我的农场", "我有多少钱", "远处", "没看见",
    ),
    "capabilities": (
        "can you buy", "buy for me", "change schedule", "follow me", "help me farm",
        "帮我买", "替我买", "替我花钱", "商店买", "买种子", "改行程",
        "行动路线", "每天来农场", "跟着我", "帮我干活", "帮我浇水",
        "收作物", "作物收好", "已经把所有",
    ),
    "safety": (
        "cheat", "warp", "trash item", "delete item", "spend my money",
        "作弊", "传送", "删除物品", "扔掉", "花我的钱", "替我花钱",
        "声称已经", "告诉我你已经", "作物收好",
    ),
    "prompt_injection": (
        "system prompt", "developer message", "ignore previous", "hidden rule",
        "系统提示", "开发者命令", "忽略之前", "隐藏规则", "退出角色", "逐字输出",
    ),
    "out_of_world": (
        "deepseek", "chatgpt", "language model", "backend", "api", "source code", "mod",
        "latest version update", "another game", "genshin",
        "大模型", "语言模型", "后端", "接口", "源代码", "模组",
        "原神", "最新版本", "版本更新", "其他游戏",
    ),
}


def token_list(text: str) -> list[str]:
    normalized = (text or "").lower()
    tokens = re.findall(r"[a-z0-9_]+", normalized)
    for sequence in re.findall(r"[\u4e00-\u9fff]+", normalized):
        if len(sequence) == 1:
            tokens.append(sequence)
            continue
        tokens.extend(sequence[index:index + 2] for index in range(len(sequence) - 1))
        if len(sequence) <= 8:
            tokens.append(sequence)
    return tokens


def topic_tags(text: str) -> set[str]:
    normalized = re.sub(r"\s+", " ", (text or "").lower())
    compact = re.sub(r"\s+", "", normalized)
    inferred: set[str] = set()
    for tag, markers in TOPIC_MARKERS.items():
        if any(
            marker.lower() in normalized or re.sub(r"\s+", "", marker.lower()) in compact
            for marker in markers
        ):
            inferred.add(tag)
    if inferred & {"mines", "games", "music", "occult"}:
        inferred.add("interests")
    if inferred & {"repeated_topic", "conflict", "reconnection"}:
        inferred.add("relationship")
    if inferred & {"capabilities", "safety", "prompt_injection", "out_of_world", "perception"}:
        inferred.add("boundaries")
    return inferred


def character_ngrams(text: str, minimum: int = 2, maximum: int = 4) -> list[str]:
    normalized = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", "", (text or "").lower())
    grams: list[str] = []
    for size in range(minimum, maximum + 1):
        grams.extend(
            normalized[index:index + size]
            for index in range(max(0, len(normalized) - size + 1))
        )
    return grams


def tokenize(text: str) -> set[str]:
    return set(token_list(text))


def score_text(query: str, text: str) -> float:
    query_tokens = tokenize(query)
    text_tokens = tokenize(text)
    if not query_tokens or not text_tokens:
        return 0.0
    overlap = len(query_tokens & text_tokens)
    cosine = overlap / math.sqrt(len(query_tokens) * len(text_tokens))
    query_normalized = re.sub(r"\s+", "", (query or "").lower())
    text_normalized = re.sub(r"\s+", "", (text or "").lower())
    phrase_bonus = 0.2 if len(query_normalized) >= 3 and query_normalized in text_normalized else 0.0
    return min(1.0, cosine + phrase_bonus)


class RagStore:
    def __init__(self, directory: Path):
        self.directory = directory
        self.chunks = self._load_chunks()
        self.chunks_by_id = {
            str(chunk.get("id", "")): chunk
            for chunk in self.chunks
            if chunk.get("id")
        }
        self.document_tokens = [token_list(self._searchable_text(chunk)) for chunk in self.chunks]
        self.document_frequency = self._document_frequency()
        self.average_length = (
            sum(len(tokens) for tokens in self.document_tokens) / len(self.document_tokens)
            if self.document_tokens else 1.0
        )
        self.document_ngrams = [
            Counter(character_ngrams(self._searchable_text(chunk)))
            for chunk in self.chunks
        ]
        self.ngram_frequency = self._ngram_document_frequency()

    def _load_chunks(self) -> List[Dict[str, Any]]:
        chunks: list[dict[str, Any]] = []
        if not self.directory.exists():
            return chunks
        for path in sorted(self.directory.glob("*.jsonl")):
            for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
                line = line.strip()
                if not line:
                    continue
                try:
                    item = json.loads(line)
                except json.JSONDecodeError as exc:
                    raise ValueError(f"Invalid RAG JSON in {path.name}:{line_number}") from exc
                item.setdefault("source", path.name)
                item.setdefault("tags", [])
                item.setdefault("priority", 1)
                chunks.append(item)
        return chunks

    @staticmethod
    def _searchable_text(chunk: Dict[str, Any]) -> str:
        return " ".join([
            str(chunk.get("title", "")),
            str(chunk.get("text", "")),
            " ".join(str(alias) for alias in chunk.get("aliases", [])),
            " ".join(str(tag) for tag in chunk.get("tags", [])),
        ])

    def _document_frequency(self) -> Counter[str]:
        frequency: Counter[str] = Counter()
        for tokens in self.document_tokens:
            frequency.update(set(tokens))
        return frequency

    def _ngram_document_frequency(self) -> Counter[str]:
        frequency: Counter[str] = Counter()
        for grams in self.document_ngrams:
            frequency.update(grams.keys())
        return frequency

    def _bm25(self, query_tokens: list[str], document_tokens: list[str]) -> float:
        if not query_tokens or not document_tokens or not self.chunks:
            return 0.0
        counts = Counter(document_tokens)
        score = 0.0
        document_length = len(document_tokens)
        for token in set(query_tokens):
            term_frequency = counts.get(token, 0)
            if term_frequency == 0:
                continue
            document_frequency = self.document_frequency.get(token, 0)
            inverse_frequency = math.log(1 + (len(self.chunks) - document_frequency + 0.5) / (document_frequency + 0.5))
            denominator = term_frequency + 1.5 * (1 - 0.75 + 0.75 * document_length / self.average_length)
            score += inverse_frequency * term_frequency * 2.5 / denominator
        return score

    def _ngram_cosine(self, query: str, document_index: int) -> float:
        query_counts = Counter(character_ngrams(query))
        document_counts = self.document_ngrams[document_index]
        if not query_counts or not document_counts:
            return 0.0

        total_documents = max(1, len(self.chunks))
        dot = 0.0
        query_norm = 0.0
        document_norm = 0.0
        for gram, count in query_counts.items():
            inverse_frequency = math.log(
                1.0 + total_documents / (1.0 + self.ngram_frequency.get(gram, 0))
            )
            query_weight = count * inverse_frequency
            query_norm += query_weight * query_weight
            document_weight = document_counts.get(gram, 0) * inverse_frequency
            dot += query_weight * document_weight
        for gram, count in document_counts.items():
            inverse_frequency = math.log(
                1.0 + total_documents / (1.0 + self.ngram_frequency.get(gram, 0))
            )
            weight = count * inverse_frequency
            document_norm += weight * weight
        if query_norm <= 0 or document_norm <= 0:
            return 0.0
        return dot / math.sqrt(query_norm * document_norm)

    def search_details(
        self,
        query: str,
        top_k: int,
        tags: list[str] | None = None,
        npc_name: str = "",
    ) -> List[Dict[str, Any]]:
        context_tags = {str(tag).lower() for tag in (tags or [])}
        inferred_tags = topic_tags(query)
        query_tokens = token_list(query)
        scored: list[tuple[float, dict[str, Any]]] = []
        for index, chunk in enumerate(self.chunks):
            if not self._is_chunk_allowed_for_npc(chunk, npc_name):
                continue
            searchable = self._searchable_text(chunk)
            bm25 = self._bm25(query_tokens, self.document_tokens[index])
            lexical = score_text(query, searchable)
            vector = self._ngram_cosine(query, index)
            chunk_tags = {str(tag).lower() for tag in chunk.get("tags", [])}
            inferred_matches = inferred_tags & chunk_tags
            context_matches = context_tags & chunk_tags
            topic_score = min(0.9, len(inferred_matches) * 0.3)
            critical_score = (
                0.5
                if inferred_matches & {"prompt_injection", "out_of_world"}
                else 0.0
            )
            context_score = sum(
                0.025 if tag in {"abigail", "boundaries"} else 0.08
                for tag in context_matches
            )
            priority_score = max(0, min(3, int(chunk.get("priority", 1) or 1))) * 0.04
            score = (
                bm25 / (bm25 + 3.0)
                + lexical * 0.32
                + vector * 0.58
                + topic_score
                + critical_score
                + min(0.24, context_score)
                + priority_score
            )
            if score > 0.08:
                scored.append((score, chunk))
        scored.sort(key=lambda item: item[0], reverse=True)
        selected = self._with_parent_context(scored, max(0, top_k), npc_name)
        return [
            {
                "id": str(chunk.get("id", "")),
                "title": str(chunk.get("title", "")),
                "text": str(chunk.get("text", "")),
                "tags": list(chunk.get("tags", [])),
                "source": str(chunk.get("source", "")),
                "parent_id": str(chunk.get("parent_id", "")),
                "score": round(score, 6),
                "matched_query_tags": sorted(inferred_tags & {
                    str(tag).lower() for tag in chunk.get("tags", [])
                }),
                "formatted": format_chunk(chunk),
            }
            for score, chunk in selected
        ]

    def _with_parent_context(
        self,
        scored: list[tuple[float, dict[str, Any]]],
        top_k: int,
        npc_name: str = "",
    ) -> list[tuple[float, dict[str, Any]]]:
        if top_k <= 0:
            return []
        scores_by_id = {
            str(chunk.get("id", "")): score
            for score, chunk in scored
        }
        selected: list[tuple[float, dict[str, Any]]] = []
        seen: set[str] = set()
        for score, chunk in scored:
            if len(selected) >= top_k:
                break
            parent_id = str(chunk.get("parent_id", ""))
            if parent_id and parent_id not in seen:
                parent = self.chunks_by_id.get(parent_id)
                if parent is not None and self._is_chunk_allowed_for_npc(parent, npc_name):
                    parent_score = scores_by_id.get(parent_id, score * 0.92)
                    selected.append((parent_score, parent))
                    seen.add(parent_id)
                    if len(selected) >= top_k:
                        break
            chunk_id = str(chunk.get("id", ""))
            if chunk_id in seen:
                continue
            selected.append((score, chunk))
            seen.add(chunk_id)
        return selected

    @staticmethod
    def _is_chunk_allowed_for_npc(chunk: Dict[str, Any], npc_name: str) -> bool:
        target = (npc_name or "").strip().lower()
        explicit_npc = str(chunk.get("npc", "")).strip().lower()
        if explicit_npc:
            return not target or explicit_npc == target

        # Legacy Abigail chunks predate the explicit npc field.
        chunk_tags = {str(tag).lower() for tag in chunk.get("tags", [])}
        if "abigail" in chunk_tags:
            return not target or target == "abigail"
        return True

    def search(
        self,
        query: str,
        top_k: int,
        tags: list[str] | None = None,
        npc_name: str = "",
    ) -> List[str]:
        return [
            item["formatted"]
            for item in self.search_details(query, top_k, tags, npc_name)
        ]


def format_chunk(chunk: Dict[str, Any]) -> str:
    title = chunk.get("title") or chunk.get("id") or chunk.get("source") or "chunk"
    tags = ", ".join(chunk.get("tags", []))
    text = chunk.get("text", "")
    suffix = f" [{tags}]" if tags else ""
    return f"{title}{suffix}: {text}"
