# Valewake Action Agent v1

## Scope

Version 0.9.0 supports two bounded NPC jobs:

- `water_crops`: water at most 10 planted, living, unwatered crop tiles.
- `clear_weeds`: clear at most 10 objects on the strict weed allowlist.

Planting, shopping, fishing, item transfer, money changes, and relationship
commands are not executable actions in this version.

## Authority pipeline

```text
player message
  -> LLM typed proposal
  -> deterministic proposal validator
  -> NPC disposition check
  -> relationship and world-state gates
  -> native Yes/No confirmation
  -> persistent Job Queue
  -> deterministic target planner
  -> movement controller
  -> typed executor
  -> postcondition check
  -> Action Trace
```

The LLM cannot call SMAPI or mutate the world. It can only return an
`action_proposal`. The C# mod rejects unsupported actions, low confidence,
ungrounded evidence, child NPCs, festivals/events, late jobs, non-host clients,
insufficient relationships, and duplicate active jobs.

## Proposal schema

```json
{
  "proposal_id": "proposal_123",
  "intent": "request_help",
  "action": "water_crops",
  "disposition": "accept",
  "parameters": {
    "location": "Farm",
    "area": "near_player",
    "max_targets": 10
  },
  "confidence": 0.8,
  "reason": "The NPC is willing to help.",
  "evidence": "Please help me water the crops.",
  "requires_confirmation": true
}
```

The backend normalizes aliases, caps `max_targets` at 10, and grounds accepted
proposals in the current player message. The local validator remains the final
authority.

## Persistent jobs

Accepted jobs are stored with SMAPI save data under
`valewake-action-jobs-v1`. A job records:

- stable job and proposal IDs;
- save, NPC, action, and state;
- immutable target limit and farm anchor;
- selected target tiles and per-target progress;
- the NPC's original location, tile, facing direction, and schedule flag;
- completion, skip, failure, and recovery messages.

The runtime state machine is:

```text
accepted -> dispatching -> preparing -> navigating -> acting
         -> completed
         -> cancelled
         -> failed_recoverable
         -> failed_terminal
```

On save reload, a nonterminal job is not replayed. It is marked
`failed_recoverable`, its NPC is restored, and the recovery is traced. This
prevents duplicate world mutations.

## Movement and schedule ownership

When a job starts, Valewake:

1. captures the NPC return context;
2. clears the current controller and sets `followSchedule = false`;
3. performs work;
4. restores the original location, tile, facing, and schedule flag;
5. calls `checkSchedule(currentTime)` when schedule following was originally on.

For an NPC already on the farm, movement uses Stardew Valley's
`PathFindController` to reach a walkable tile adjacent to each target.

Cross-map dispatch does not pretend to simulate every intermediate map. It
waits for the player to leave the NPC's screen, with a short timeout, then moves
the reserved NPC to a legal farm entry tile. All visible farm movement after
entry uses real local pathfinding. Completion, cancellation, path failure, or
reload restores the schedule context.

## Executors and allowlists

Watering selects only `HoeDirt` which:

- contains a crop;
- contains a living crop;
- is not already watered;
- is within the configured radius for a local farm request.

Weeding selects only objects whose qualified item ID is in the weed allowlist,
or whose internal name is exactly `Weeds`. Crops, grass, trees, saplings,
fences, chests, machines, decorations, and placed items are excluded.

Each target is revalidated immediately before mutation. A moved, destroyed, or
already-completed target is skipped. Watering verifies the final `HoeDirt`
state. Weeding uses the normal scythe tool-action path and verifies removal.

## Failure and multiplayer policy

- Only `Context.IsMainPlayer` may authorize or execute jobs.
- Farmhands can still chat, but their action proposal is rejected locally.
- Events and festivals block job creation.
- Jobs cannot start at or after 22:00.
- Path timeout skips only the current target; the queue continues.
- Missing NPCs, map changes, cancellation, and reload restore the captured
  schedule context where possible.
- `agent_cancel_jobs` is an operator escape hatch.

Full multiplayer proposal forwarding and synchronized client UI are not part of
v1. The host is the sole world-mutation authority.

## Trace

Append-only records are written to:

```text
Mods/Valewake/data/traces/action_trace.jsonl
```

The trace includes proposal validation, confirmation, acceptance, transitions,
target completion/skips, cancellation, failure, completion, and reload
recovery. The SMAPI commands `agent_jobs` and `agent_cancel_jobs` expose the
active queue for testing.
