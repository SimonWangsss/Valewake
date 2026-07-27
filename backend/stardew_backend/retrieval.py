import json
import math
import re
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List


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
        self.document_tokens = [token_list(self._searchable_text(chunk)) for chunk in self.chunks]
        self.document_frequency = self._document_frequency()
        self.average_length = (
            sum(len(tokens) for tokens in self.document_tokens) / len(self.document_tokens)
            if self.document_tokens else 1.0
        )

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
            " ".join(str(tag) for tag in chunk.get("tags", [])),
        ])

    def _document_frequency(self) -> Counter[str]:
        frequency: Counter[str] = Counter()
        for tokens in self.document_tokens:
            frequency.update(set(tokens))
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

    def search_details(
        self,
        query: str,
        top_k: int,
        tags: list[str] | None = None,
    ) -> List[Dict[str, Any]]:
        requested_tags = {str(tag).lower() for tag in (tags or [])}
        query_tokens = token_list(query)
        scored: list[tuple[float, dict[str, Any]]] = []
        for index, chunk in enumerate(self.chunks):
            searchable = self._searchable_text(chunk)
            bm25 = self._bm25(query_tokens, self.document_tokens[index])
            lexical = score_text(query, searchable)
            chunk_tags = {str(tag).lower() for tag in chunk.get("tags", [])}
            matching_tags = requested_tags & chunk_tags
            tag_score = min(0.54, len(matching_tags) * 0.18)
            priority_score = max(0, min(3, int(chunk.get("priority", 1) or 1))) * 0.04
            score = bm25 / (bm25 + 3.0) + lexical * 0.45 + tag_score + priority_score
            if score > 0.08:
                scored.append((score, chunk))
        scored.sort(key=lambda item: item[0], reverse=True)
        return [
            {
                "id": str(chunk.get("id", "")),
                "title": str(chunk.get("title", "")),
                "text": str(chunk.get("text", "")),
                "tags": list(chunk.get("tags", [])),
                "source": str(chunk.get("source", "")),
                "score": round(score, 6),
                "formatted": format_chunk(chunk),
            }
            for score, chunk in scored[:max(0, top_k)]
        ]

    def search(self, query: str, top_k: int, tags: list[str] | None = None) -> List[str]:
        return [
            item["formatted"]
            for item in self.search_details(query, top_k, tags)
        ]


def format_chunk(chunk: Dict[str, Any]) -> str:
    title = chunk.get("title") or chunk.get("id") or chunk.get("source") or "chunk"
    tags = ", ".join(chunk.get("tags", []))
    text = chunk.get("text", "")
    suffix = f" [{tags}]" if tags else ""
    return f"{title}{suffix}: {text}"
