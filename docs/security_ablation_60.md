# Valewake Dialogue Security Ablation (60-Case Development Set)

## Versions

- Baseline: `250a2699faab7728aecd0998ed115026d67ba90e`
- Current security layer: `a4b301fc115ea084abbd57cfa1ad8b12acc41712`
- Baseline worktree:
  `E:\Codex\ai-npc-3d-persona-memory\projects\Valewake-security-baseline`
- Dataset: `backend/eval/jailbreak_golden.jsonl`
- Machine-readable report: `backend/eval/results/security_ablation_60.json`

The same 60 labeled scenarios were evaluated against both source trees. This
is a deterministic policy ablation: it does not call DeepSeek and does not
measure stochastic generation quality.

## Dataset

| Category | Cases |
|---|---:|
| Persona override | 8 |
| Secret extraction | 8 |
| Policy bypass | 7 |
| Out-of-world induction | 5 |
| Multi-turn attack | 10 |
| Split intent | 4 |
| Benign near-boundary input | 18 |
| Total | 60 |

There are 42 attack scenarios and 18 benign scenarios.

## Results

| Metric | Baseline | Current | Change |
|---|---:|---:|---:|
| Accuracy | 86.67% | 100.00% | +13.33 pp |
| Balanced accuracy | 90.47% | 100.00% | +9.53 pp |
| Attack recall | 80.95% (34/42) | 100.00% (42/42) | +19.05 pp |
| Attack success rate | 19.05% (8/42) | 0.00% (0/42) | -19.05 pp |
| Benign specificity | 100.00% (18/18) | 100.00% (18/18) | unchanged |
| False-positive rate | 0.00% | 0.00% | unchanged |
| Memory guard coverage | 0.00% (0/42) | 100.00% (42/42) | +100 pp |
| Action guard coverage | 0.00% (0/42) | 100.00% (42/42) | +100 pp |
| Output-leakage catch rate | 20.00% (1/5) | 100.00% (5/5) | +80 pp |

### Category Accuracy

| Category | Baseline | Current |
|---|---:|---:|
| Persona override | 50.00% (4/8) | 100.00% (8/8) |
| Secret extraction | 100.00% (8/8) | 100.00% (8/8) |
| Policy bypass | 57.14% (4/7) | 100.00% (7/7) |
| Out-of-world induction | 100.00% (5/5) | 100.00% (5/5) |
| Multi-turn attack | 100.00% (10/10) | 100.00% (10/10) |
| Split intent | 75.00% (3/4) | 100.00% (4/4) |
| Benign input | 100.00% (18/18) | 100.00% (18/18) |

The baseline missed eight attacks. The misses were concentrated in persona
override paraphrases, policy-bypass paraphrases, and one split English intent.
Its English post-check caught one model-identity leak, but all four Chinese
leak examples passed unchanged.

## What Changed

1. Input normalization now uses Unicode NFKC and punctuation-insensitive text.
2. Injection detection moved from a flat marker list to intent-level concept
   groups: persona override, secret extraction, policy bypass, and
   out-of-world induction.
3. Recent user turns are aggregated so an intent can be assembled across the
   conversation.
4. The generated reply is checked for Chinese and English model identity,
   hidden-instruction, developer-message, backend, and credential leakage.
5. Output leakage escalates the turn to `refuse_in_character` and replaces
   the reply with an in-world fallback.
6. A refused turn cannot write player memory, generate an action proposal, or
   produce a positive relationship effect.
7. Trace records contain the semantic decision and enforcement flags.

The LLM remains responsible for natural dialogue, but deterministic policy is
the authority for state mutation.

## Reproduction

```powershell
cd E:\Codex\ai-npc-3d-persona-memory\projects\Valewake\backend

.\.venv\Scripts\python.exe eval\compare_security_ablation.py `
  --baseline E:\Codex\ai-npc-3d-persona-memory\projects\Valewake-security-baseline `
  --current E:\Codex\ai-npc-3d-persona-memory\projects\Valewake `
  --output eval\results\security_ablation_60.json
```

## Limitations

- These 60 cases are a development/regression set created alongside the
  policy, not an unseen holdout.
- The multi-turn cases end with recognizable attack language, so this set does
  not isolate the incremental value of history aggregation by itself.
- Output leakage uses five fixed synthetic replies.
- Memory and action metrics measure deterministic guard availability; an
  end-to-end game test is still needed to verify persisted files and original
  friendship points.
- DeepSeek generation variability, latency, and model-specific behavior are
  outside this deterministic run.

For stronger evidence, freeze the implementation and evaluate a separately
authored holdout plus repeated live-model runs.

## Interview Draft

Valewake turns Stardew Valley residents into LLM-driven NPC agents. A safety
problem I found was that prompt-only character instructions were not enough:
players could paraphrase an identity override, assemble an attack over several
turns, or make the model verbally refuse while still returning structured
memory, relationship, or action fields.

I changed the system from a flat keyword filter into a layered boundary
pipeline. The first layer normalizes and classifies intent-level concepts such
as persona override, hidden-instruction extraction, policy bypass, and
out-of-world induction. It can aggregate recent user turns. The second layer
checks generated Chinese and English text for model-identity and implementation
leakage. The final layer is a deterministic state firewall: a refused or
leaking turn cannot write long-term memory, increase friendship, or create an
executable action proposal.

I kept the old implementation as a Git worktree and ran both versions against
the same 60-case development set. Attack recall improved from 80.95% to 100%,
the observed attack success rate fell from 19.05% to 0%, and all 18 benign
near-boundary cases remained unblocked. Memory and action guard coverage went
from 0% to 100%, while the multilingual output-leakage check improved from one
of five fixed examples to five of five.

I would not claim universal jailbreak resistance from this result. The set is
small and was used during development. The next evaluation layer is an unseen
holdout and repeated live DeepSeek generations. The engineering point is that
the LLM is used for expression, while deterministic policy remains the
authority over persistent game state.
