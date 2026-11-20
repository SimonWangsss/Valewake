"""Shared text relevance engine; callers own authorization and business ranking."""
import math
import re
from collections import Counter

from stardew_backend.retrieval_concepts import concepts, concept_score


def token_list(text: str) -> list[str]:
    normalized = (text or "").lower()
    tokens = re.findall(r"[a-z0-9_]+", normalized)
    for sequence in re.findall(r"[\u4e00-\u9fff]+", normalized):
        if len(sequence) == 1:
            tokens.append(sequence)
            continue
        tokens.extend(sequence[i:i + 2] for i in range(len(sequence) - 1))
        if len(sequence) <= 8:
            tokens.append(sequence)
    return tokens


def character_ngrams(text: str, minimum: int = 2, maximum: int = 4) -> list[str]:
    normalized = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", "", (text or "").lower())
    return [normalized[i:i + size] for size in range(minimum, maximum + 1)
            for i in range(max(0, len(normalized) - size + 1))]


class HybridTextIndex:
    version = "hybrid-text-v1"

    def __init__(self, texts: list[str], concept_texts: list[str] | None = None):
        self.document_tokens = [token_list(t) for t in texts]
        self.document_counts = [Counter(t) for t in self.document_tokens]
        self.document_token_sets = [set(t) for t in self.document_tokens]
        self.normalized_documents = [re.sub(r"\s+", "", t.lower()) for t in texts]
        self.document_frequency = Counter(t for tokens in self.document_token_sets for t in tokens)
        self.average_length = sum(map(len, self.document_tokens)) / len(texts) if texts else 1
        self.document_ngrams = [Counter(character_ngrams(t)) for t in texts]
        self.ngram_frequency = Counter(g for counts in self.document_ngrams for g in counts)
        total = max(1, len(texts))
        self.ngram_idf = {g: math.log(1 + total / (1 + count)) for g, count in self.ngram_frequency.items()}
        self.unseen_ngram_idf = math.log(1 + total)
        self.document_ngram_norms = [sum((count * self.ngram_idf[g]) ** 2 for g, count in counts.items()) for counts in self.document_ngrams]
        self.document_concepts = [concepts(t) for t in (texts if concept_texts is None else concept_texts)]
        if len(self.document_concepts) != len(texts):
            raise ValueError("Concept text count must match document count")

    @staticmethod
    def prepare(query: str) -> dict:
        tokens = token_list(query)
        return {"tokens": tokens, "set": set(tokens), "normalized": re.sub(r"\s+", "", query.lower()),
                "ngrams": Counter(character_ngrams(query)), "concepts": concepts(query)}

    def bm25(self, query_tokens, document_tokens, counts=None):
        if not query_tokens or not document_tokens or not self.document_tokens:
            return 0.0
        counts = Counter(document_tokens) if counts is None else counts
        result = 0.0
        for token in set(query_tokens):
            tf = counts.get(token, 0)
            if not tf:
                continue
            df = self.document_frequency.get(token, 0)
            idf = math.log(1 + (len(self.document_tokens) - df + .5) / (df + .5))
            denominator = tf + 1.5 * (1 - .75 + .75 * len(document_tokens) / self.average_length)
            result += idf * tf * 2.5 / denominator
        return result

    def ngram_cosine(self, query, index, query_counts=None):
        query_counts = Counter(character_ngrams(query)) if query_counts is None else query_counts
        counts = self.document_ngrams[index]
        dot = qnorm = 0.0
        for gram, count in query_counts.items():
            idf = self.ngram_idf.get(gram, self.unseen_ngram_idf)
            weight = count * idf
            qnorm += weight * weight
            dot += weight * counts.get(gram, 0) * idf
        dnorm = self.document_ngram_norms[index]
        return dot / math.sqrt(qnorm * dnorm) if qnorm and dnorm else 0.0

    def score(self, prepared: dict, index: int) -> dict:
        bm25 = self.bm25(prepared["tokens"], self.document_tokens[index], self.document_counts[index])
        qset, dset = prepared["set"], self.document_token_sets[index]
        lexical = len(qset & dset) / math.sqrt(len(qset) * len(dset)) if qset and dset else 0.0
        if qset and dset and len(prepared["normalized"]) >= 3 and prepared["normalized"] in self.normalized_documents[index]:
            lexical = min(1.0, lexical + .2)
        vector = self.ngram_cosine("", index, prepared["ngrams"])
        concept = concept_score(prepared["concepts"], self.document_concepts[index])
        # Tiny character overlaps alone are not evidence of relevance.
        supported = bool(bm25 or lexical or concept or vector >= .15)
        total = bm25 / (bm25 + 3) + lexical * .32 + vector * .58 + concept * .65 if supported else 0.0
        return {"bm25": bm25, "lexical": lexical, "character_tfidf": vector,
                "concept": concept, "relevance": total}
