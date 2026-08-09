# Valewake 0.10.0 Evaluation Snapshot

Generated on 2026-08-09. Results from deterministic tests and real-game traces are
reported separately. A passing offline rule does not prove that SMAPI pathfinding,
animation, drops, or schedule restoration worked in the game.

## Current Results

| Layer | Dataset | Metric | Result | Interpretation |
|---|---:|---|---:|---|
| Dialogue/RAG/Memory | 84 deterministic cases | Pass rate | 100% (84/84) | 48 live-generation cases were intentionally skipped |
| Lore retrieval | 40 cases | Runtime gold recall@5 | 93.8% | Required chunks retrieved in the actual no-hint route |
| Lore retrieval | 40 cases | Runtime MRR | 80.1% | Relevant evidence usually ranks near the top |
| Security boundary | 60 cases | Balanced accuracy | 90.47% -> 100% | Same development set, baseline worktree versus current |
| Security boundary | 42 attacks | Attack recall | 80.95% -> 100% | Eight former misses reduced to zero on this set |
| Security boundary | 18 benign cases | False-positive rate | 0% -> 0% | No additional benign refusal on this set |
| Action routing | 48 cases | Exact intent accuracy | 83.33% -> 100% | Frozen before the routing fixes in this iteration |
| Action routing | 48 cases | Macro-F1 | 84.46% -> 100% | Covers seven actions plus no-action dialogue |
| Action routing | 12 negative cases | False-positive rate | 16.67% -> 0% | Fixed preference/past-event statements triggering jobs |
| Action proposal | 36 action cases | Contract pass rate | 83.33% -> 100% | Evidence, cap, confirmation, action and parameters |
| Real dialogue trace | 23 turns | Structural invariant rate | 100% | Safety/shape checks only, not naturalness scoring |
| Real farm trace | 7 jobs | Terminal coverage | 100% (7/7) | Every historical accepted job reached a terminal event |
| Real farm trace | 7 jobs | State completion rate | 85.71% (6/7) | Includes two completed jobs that did no useful work |
| Real farm trace | 7 jobs | Productive success rate | 57.14% (4/7) | At least one target actually completed |
| Real farm trace | 32 target outcomes | Target success rate | 71.88% (23/32) | 23 completed, 9 skipped |
| Real mine trace | 0 expeditions | Execution success | N/A | No in-game expedition evidence yet |

## Important Limits

- The 48-case action set and 60-case security set are project-authored development
  sets. Their before/after comparisons are valid regression evidence, but 100% must not
  be presented as general real-world accuracy.
- The seven farm jobs span multiple implementation revisions and are below the suggested
  minimum sample of 20 terminal jobs per action/version.
- Current real trace scoring can verify selected target type, linkage, terminal state,
  schedule/location restoration fields, and executor-reported allowlist decisions for new
  traces. It does not yet compare a full game-state snapshot before and after every action.
- Open-ended dialogue quality still needs blind human review or a separately validated judge.

## Functional Agent Benchmark

Use a fixed save and reset it before every trial. Record at least 20 trials for each action
and publish both numerator and denominator, not only a percentage.

| Capability | Trial strata | Primary metrics |
|---|---|---|
| Water crops | NPC on farm/in farmhouse/outside; player on/off farm; 1/3/10 targets; blocked paths | Productive task success, target success, P95 completion time, path retry rate, schedule restore rate |
| Clear weeds | Mixed weeds and protected objects; blocked/unblocked paths | Target precision, target recall, protected-object damage count, task success |
| Follow mines | Mine entrance and at least three level transitions | Transition success, follow recovery latency, duplicate-NPC count, clean leave rate |
| Defend player | Threats inside/outside defense radius; multiple monsters | Threat precision, nearby-threat recall, damage/kill contribution, excessive pursuit rate |
| Mine target | Allowed rock, ore node, chest, machine, empty tile | Target precision, normal-drop verification, unintended-object damage count |
| Mine nearby | Mixed nodes, obstacles, monsters, resource priorities | Productive success, node precision, priority hit rate, defense preemption rate |

Recommended release gates after collecting enough data:

- Productive task success >= 90% with at least 20 trials per action.
- Target precision = 100% for destructive actions.
- Schedule/location restoration >= 95%.
- Unintended object/NPC damage = 0.
- Cross-level follow success >= 95%.
- No-op completed jobs <= 5%.

## Other Strong Evaluation Targets

| Module | Recommended objective metrics |
|---|---|
| Memory | Write precision, durable-fact recall@5, contradiction resolution, save rollback correctness, cross-save/NPC leakage rate |
| RAG | Recall@5, MRR, context precision, answer faithfulness, unsupported-fact rate |
| Perception | Field-level exact match, stale-state rate, invisible/far-state leakage rate |
| Relationship | Evidence-grounded change rate, daily-cap compliance, false positive change rate |
| Persona | Blind 0-2 rubric for persona, relevance, naturalness and faithfulness; two-rater agreement |
| Runtime | API success, structured-JSON validity, P50/P95 latency, retries, token/cost per successful interaction |

For interview presentation, show four separate panels: dialogue quality, retrieval/memory,
safety, and action execution. Do not average them into one opaque overall score.
