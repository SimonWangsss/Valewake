# Valewake Action Catalog

All actions begin from normal AI dialogue, remain subject to the NPC's in-character
decision, pass deterministic local validation, and require the player to confirm the
native Yes/No prompt. The LLM never controls movement, tool swings, object deletion,
combat hits, or money directly.

## Farm jobs

### Water crops

- Example: `你能帮我给农场里的作物浇水吗？`
- Limit: at most 10 eligible dry crop tiles; dead crops and already watered soil are skipped.
- Execution: the NPC travels to the farm, walks to each target, plays the watering effect,
  and can continue off-screen.
- Ends: target list completes, no eligible target exists, 22:00 is reached, the day/save
  changes, a recoverable failure occurs, or `agent_cancel_jobs` is used.

### Clear weeds

- Example: `帮我清理农场里的杂草吧。`
- Limit: at most 10 objects on the strict weed allowlist. Crops, machines, fences,
  decorations, trees, and placed items are excluded.
- Execution and end conditions: the same farm Job Queue lifecycle as watering.

### Chop mature trees

- Example: `可以帮我砍几棵树吗？`
- Limit: at most 3 mature wild trees on the farm. Fruit trees, saplings, tapped trees,
  and arbitrary placed objects are excluded.
- Execution: the NPC uses repeated visible axe swings, finishes the trunk and stump,
  and relies on the base game's tree drop logic.
- Ends: all selected trees complete or are skipped, or any standard farm-job end
  condition occurs.

## Mine expedition

### Join, mine, and defend

- Example: `陪我下矿。`
- Requirement: adult social NPC, at least 4 hearts by default, before 23:00.
- One confirmation starts a continuous session with this priority:
  nearby hostile monster, manual `G` target, autonomous eligible mine node, player follow.
- Mining is limited to 10 nodes per expedition by default and uses visible pickaxe swings.
- Defense only considers hostile monsters near the player and does not pursue far targets.
- Cross-map following, schedule reservation, speed restoration, and trace recording remain
  active for the whole session.
- Ends: `agent_end_expedition`, 23:00, reload, or unrecoverable controller failure.

### Manual mine priority

- During an expedition, point at a rock and press `G`.
- Selection checks the cursor tile first, then snaps to the nearest non-big-craftable
  `IsBreakableStone()` within one tile.
- A manual target interrupts autonomous mining but does not end the expedition afterward.

## Not executable yet

### Shopping and purchases

Shopping remains intentionally blocked. A safe implementation needs a typed item ID,
quantity, maximum budget, shop and opening-hours validation, inventory-space checks,
an exact-price confirmation, and atomic charge/refund behavior. NPC dialogue must not
claim a purchase was completed until that local transaction executor exists.

## Inspection commands

- `agent_jobs`: show active farm jobs.
- `agent_cancel_jobs`: cancel farm jobs and restore NPC schedules.
- `agent_expedition`: show the active mine expedition.
- `agent_end_expedition`: end the mine expedition and restore the NPC.
- `agent_mark action_good ...` / `agent_mark action_bad ...`: annotate the latest turn.
