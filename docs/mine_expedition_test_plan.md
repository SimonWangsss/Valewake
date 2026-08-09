# Mine Expedition Test Plan

## Preconditions

- Start Stardew Valley through SMAPI and load a host save.
- Use an adult social NPC with at least 4 hearts.
- Keep the NPC nearby for the invitation; enter the regular Mines for mining tests.
- `EnableMineExpeditions` is true. The default priority-target key is `G`.

Use `agent_expedition` after each step and inspect
`Mods/Valewake/data/traces/expedition_trace.jsonl` after the game closes.

## Session model and end conditions

One NPC owns one persistent Expedition Session. By default, accepting
`join_mine_expedition` enables following, nearby defense, and autonomous mining in that
same session. The local priority order is hostile threat, manual `G` target, autonomous
mine target, then following. These are controller states, not competing jobs.

- A standalone `mine_target` ends after the selected node is processed.
- A standalone `mine_nearby` ends when its quota is reached or no eligible node remains.
- After a target is mined or skipped as unreachable, the controller selects the next
  eligible task or returns to following.
- Follow/defense/full expedition ends through `agent_end_expedition`, at 23:00 by
  default, on save reload, or after an unrecoverable controller failure.

## 1. Join and follow

Say `陪我下矿。`, accept the native Yes/No confirmation, then walk around the NPC's
current indoor map before leaving through the door. The NPC should follow at boosted
walking speed. On the outdoor map, they should arrive near the matching entrance when
one can be resolved. Enter the Mines and change at least two levels.

Pass criteria:

- One active expedition only; no duplicate NPC is spawned.
- The NPC does not remain in place while repeatedly changing direction.
- `followed_across_location` appears once per transition.
- Dynamic mine levels may use an offscreen catch-up point near the player when no reverse
  map warp exists.
- `agent_end_expedition` starts return behavior; the NPC remains visible in a mine level
  until the player exits it, then their original schedule and speed are restored.

## 2. Automatic defense

After accepting `陪我下矿。`, enter a level with monsters and approach one. No second
dialogue request is required: the existing companion should interrupt mining, defend
within the local radius, then resume the prior expedition behavior.

Pass criteria:

- Mining pauses while a nearby threat exists.
- `monster_defeated` increments only when the NPC's hit defeats a monster.
- The controller never attacks NPCs, farm animals, or non-monster characters.
- Moving away stops long pursuit beyond the configured six-tile defense radius.

## 3. Prioritize a mining target with G

During an active expedition, enter a mine level, point the tile cursor at a normal rock
or ore node, and press `G` (or run `agent_mine_target`). Exact tile selection is preferred;
the controller may snap to the nearest breakable stone within one tile.

Pass criteria:

- The HUD confirms the selected tile and remaining quota in Chinese.
- If selection fails, the HUD distinguishes no companion, wrong map, invalid cursor
  target, and exhausted quota.
- Only a non-big-craftable `Object.IsBreakableStone()` node is selected.
- The NPC paths adjacent to the node, destroys only that requested node, then resumes
  following instead of ending the parent expedition.
- Normal debris/drop logic runs with the host player as the tool user.

## 4. Autonomous nearby mining

After accepting `陪我下矿。`, enter a level containing eligible nodes within eight tiles
of the player. The companion should begin mining without another dialogue request.

Pass criteria:

- One stable adjacent stand tile is retained for each target instead of being recomputed
  from the NPC's changing position every decision tick.
- No chests, machines, placed items, ladders, crops, or decorations are touched.
- No more than 10 targets are accepted, regardless of model output.
- A node is temporarily skipped for the current level after every adjacent path candidate
  fails or the strike safety limit is reached.
- When this batch ends, the NPC resumes following because the parent session remains active.

## 5. High-level expedition

With no active session, say `陪我下矿探险，优先找铁矿，也保护我。` Confirm and enter
a mine level containing mixed nodes. The local controller should rank iron nodes first
when they are in range, while following the player and defending against nearby monsters.
The LLM must never choose per-frame movement, attacks, or tool swings.

Pass criteria:

- The proposal contains `mine_expedition` and `resource_priority=iron`.
- Trace linkage contains the same root `turn_id` and `proposal_id`, plus the latest
  capability command IDs, from validation through execution events.
- At 23:00 the expedition enters returning state.

## Known first-runtime checks

Automated tests validate policy normalization, proposal contracts, trace linkage, save
boundaries, and build compatibility. Sprite timing, map-specific collision, monster
drops, node drops, entrances on modded maps, and the feel of boosted walking speed still
require in-game observation. Treat failures as traceable controller bugs; do not widen
the local target allowlist as a shortcut.
