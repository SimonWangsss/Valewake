# Action Agent v1 Manual Test Plan

## Preconditions

- Launch Stardew Valley through SMAPI and load a save as the host.
- Use a social adult NPC with at least 2 hearts.
- Keep `EnableActionAgent`, the target action, and cross-map dispatch enabled.
- Prepare planted unwatered crops and ordinary weeds on the farm.
- Test before 22:00 and outside festivals or events.

## Local watering

1. Put a social NPC on the farm, or use a spouse who is already there.
2. Stand near the NPC and right-click through the vanilla line into Valewake.
3. Ask: `Can you help me water the nearby crops?`
4. If the NPC agrees, close their reply and choose `Yes` in the confirmation.
5. Observe local pathfinding, per-tile sound/action, and at most 10 watered crop
   tiles.
6. Run `agent_jobs` in the SMAPI console.

Expected: only planted living unwatered crops are selected. The job completes
or reports skipped unreachable targets without touching other terrain.

## Cross-map watering

1. Find an eligible NPC away from the farm and ask for watering help.
2. Confirm the job and leave the current screen, then go to the farm.
3. Observe the NPC enter at a farm entry, navigate locally, work, then return to
   the captured context and resume schedule behavior.

Expected: no visible fake walk through every intermediate map. Farm movement is
path-driven. The job never starts for a farmhand client.

## Strict weeding

1. Put ordinary weeds near the farm work area, plus a chest, crop, grass, tree,
   fence, and machine as negative controls.
2. Ask: `Can you clear the weeds on my farm?`
3. Confirm and observe at most 10 targets.

Expected: only allowlisted weeds are removed. Every negative-control object
remains unchanged.

## Failure and recovery

- During a job, run `agent_cancel_jobs`.
- Block one target's adjacent tiles and retry.
- Exit and reload while a job is nonterminal.
- Try at 22:00, during a festival/event, with a child NPC, below 2 hearts, and
  from a multiplayer farmhand.

Expected: cancellation restores the NPC. Unreachable targets are skipped. A
reloaded job is marked `failed_recoverable` and is not replayed. Every rejected
case makes no world change.

Inspect:

```text
E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\action_trace.jsonl
```
