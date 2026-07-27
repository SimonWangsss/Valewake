# Dialogue System 0.7.0 Evaluation

Date: 2026-07-27

This release keeps the frozen 132-case v2 golden set so its results remain
comparable with the 0.6.0 baseline. It also adds 11 focused v3 unit tests for
new retrieval, memory, and policy behavior.

## Architecture changes

- Expanded Lore from 22 to 54 atomic JSONL chunks.
- Added bilingual deterministic topic routing.
- Added BM25 and character n-gram TF-IDF sparse-vector retrieval.
- Added `parent_id` expansion so precise facts retain their foundation context.
- Upgraded Memory storage to schema v3.
- Added semantic deduplication, polarity, contradiction supersession, and status.
- Added a high-importance durable player profile above query-specific memory.
- Rejects questions and conversational meta-statements as durable memory.
- Added Chinese preference and opinion extraction.
- Added Chinese prompt-injection markers.
- Added one bounded low-temperature retry when a Chinese input gets a non-Chinese reply.
- Added local emotion constraints for clearly dangerous topics.
- Trace records now retain Lore IDs, scores, query tags, source, and parent IDs.

## Frozen deterministic results

| Metric | 0.6.0 | 0.7.0 |
|---|---:|---:|
| Lore cases | 17/40 | 40/40 |
| Runtime Gold Recall@5 | 0.388 | 0.938 |
| Runtime MRR | 0.224 | 0.801 |
| Policy | 19/20 | 20/20 |
| Memory deterministic | 24/24 | 24/24 |

These RAG gains were measured after increasing the corpus from 22 to 54 chunks,
so the new retriever operated with more distractors than the baseline.

## Frozen live results

Full DeepSeek run before the final two deterministic guards:

| Metric | 0.6.0 | 0.7.0 run |
|---|---:|---:|
| Live hard-check pass | 27/48 | 37/48 |
| Same-language check | 33/48 | 48/48 |
| Required-fact keyword check | 33/48 | 39/48 |
| Forbidden terms absent | 46/48 | 48/48 |
| Emotion check | 46/48 | 47/48 |
| Memory-write expectation | 46/48 | 47/48 |
| Mean latency | 7902.6 ms | 8061.2 ms |
| Max latency | 10100.1 ms | 13479.8 ms |

After this run, the remaining `live_memory_opinion` and `live_lore_danger`
functional failures received deterministic fixes and both passed targeted live
regression. The full 48-case number is intentionally not rewritten from those
targeted checks.

## Interpretation

The remaining generation failures are mostly exact `required_any` keyword
misses. Several replies are semantically valid paraphrases, such as saying that
Abigail has no way to see the distant farm without using the exact phrase
"看不到". They should remain visible until a v3 evaluation set adds semantic
rubrics or calibrated human review. Production prompts should not force fixed
test phrases merely to improve this number.

The current vector layer is local sparse retrieval, not a neural embedding
model. This avoids another API key, startup indexing, and a much larger packaged
backend. Dense embeddings should be evaluated when the corpus reaches multiple
NPCs or roughly 200+ chunks; the frozen set provides the comparison point.
