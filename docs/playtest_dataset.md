# Playtest Dataset Capture

## What is recorded

Every backend reply already creates one row in `agent_trace.jsonl`, even when the
player later exits without saving. This is intentional: raw traces are an audit
log, while save events describe whether the corresponding game session became
durable.

The linkage chain is:

```text
turn_id -> proposal_id -> job_id or expedition_id -> outcome events
```

All trace files now live together under `Valewake/data/traces` in the deployed mod:

- `agent_trace.jsonl` (backend): prompt inputs, retrieval, policy, reply, memory writes, proposal.
- `dataset_events.jsonl` (backend): save commit/rollback and applied relationship outcomes.
- `dataset_annotations.jsonl` (backend): human labels made with `agent_mark`.
- `action_trace.jsonl` (SMAPI): farm proposal, confirmation, pathing, target, and completion events.
- `expedition_trace.jsonl` (SMAPI): follow, map transition, combat, mining, return, and failure events.

## Annotating real play

Immediately after a reply, enter one label in the SMAPI console:

```text
agent_mark keep <optional note>
agent_mark reject <optional note>
agent_mark boundary <optional note>
agent_mark memory_good <optional note>
agent_mark memory_bad <optional note>
agent_mark action_good <optional note>
agent_mark action_bad <optional note>
```

The command always marks the latest reply shown in the current loaded save. The
pointer is cleared when another save loads or the game returns to title.

## Save semantics

- Saving emits `save_committed` after the memory checkpoint succeeds.
- Loading a save emits `save_loaded_rollback` after unsaved memory is discarded.
- Returning to title without saving emits `save_rolled_back`.
- The exporter assigns each turn to the first later save boundary for that save.
- Rolled-back turns remain available for failure analysis but are excluded by default.

## Export

Run from the repository root after closing the game/backend so the files are stable:

```powershell
backend\.venv\Scripts\python.exe backend\tools\export_dataset.py `
  --trace "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\agent_trace.jsonl" `
  --events "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\dataset_events.jsonl" `
  --annotations "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\dataset_annotations.jsonl" `
  --action-trace "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\action_trace.jsonl" `
  --expedition-trace "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\expedition_trace.jsonl" `
  --output "exports\playtest_dataset.jsonl"
```

Useful filters:

- `--only-marked keep`: export only human-approved turns.
- `--only-marked boundary`: export boundary examples for security evaluation.
- `--include-rolled-back`: include conversations from abandoned game sessions.
- `--no-anonymize`: retain raw save/session/player identifiers for local debugging only.

The default export pseudonymizes player/session identifiers and removes exact duplicate
NPC-input-reply triples. Keep the raw traces private because player text may contain
personal information.
