# Valewake v1.1.0

Windows release notes for GitHub and Nexus Mods.

## What's New

- Custom NPC persona packs: bind a local persona and optional lore file to an existing NPC's internal name. Use `agent_npcs` to export runtime names. This enables user-authored dialogue setup for expansion characters; it does not guarantee custom-map or task compatibility. See CUSTOM_NPCS_zh.md in the package for setup.
- Expanded scheduled meetups with outdoor companionship, a dedicated End Meeting button, farewell confirmation, and return-to-schedule behavior.
- Today's Dialogue: review the current day's conversation without losing your draft.
- NPCs can refer to gifts actually received after this update, and vanilla dialogue pages and choices you have actually seen. Earlier unrecorded events cannot be recovered.
- Switch from an active meetup or its return trip to a mine expedition after confirmation, without two controllers taking over the same NPC.
- Longer chat input: horizontal scrolling, a character counter, and a configurable limit of 600 characters by default.
- Reworked multi-character lore and shared retrieval infrastructure for lore and personal memories, while retaining separate access rules.

## Fixes and Improvements

- Improved invitation recognition across follow-up messages, short confirmations, and time/location changes.
- Fixed several Chinese time interpretation cases and mismatches between spoken appointment times and the confirmation menu.
- Removed repetitive appointment-summary suffixes and technical parenthetical rejection messages. Unavailable arrangements are handled through natural negotiation instead.
- Improved meetup landing checks, obstacle-aware movement, follow speed, safe catch-up, and recovery after chatting.
- Cleared leftover activity animations when taking over NPC movement.
- Improved return routing, alternate exits, obstruction retries, and selection of the NPC's current schedule destination instead of a stale pre-meeting position.
- Removed the experimental departure 20 minutes before a meeting. NPC movement is not taken over before the appointment time.

## Important Notes

- Windows package only. macOS/Linux are not yet supported by the bundled backend.
- Existing farming, tree chopping, mine combat and mining features remain available; they are not new in v1.1.
- Regular meetup following supports Town, Beach, Mountain, Forest and Farm. Indoor locations, custom maps and multiplayer need further validation.
- Fishing-themed meetings do not include automatic NPC fishing.
- Some offscreen transfers and safe catch-up remain; this is not continuous walking across the entire world.
- Automatic tests do not guarantee perfect in-game pathfinding. A shutdown logging exception has also been reported and remains a known issue; do not describe it as fixed.
- The bundled backend may be flagged by security software. Check the download source, published file hash and specific detection; do not disable protection simply to run it.

## Updating

Close the game and backend, back up your save, and move the old Valewake folder outside Mods. Extract the new package but do not start the game yet. Double-click `Upgrade-Valewake.cmd` in the new folder and select the old backup (the mod root or its data folder).

The tool imports Memory, Trace, user-authored NPC packs, and the provider, API key, model and API URL from the old `config.json`. Other settings keep their new defaults. API keys stored only in `.env` must be entered again. The tool does not overwrite new lore, built-in personas, meetup spots or binaries, and does not merge conflicting runtime records. See `UPGRADE_zh.md` for details and an English quick guide.

No personal memories, traces or API keys are included. Runtime records are created while playing.

For bug reports, include the NPC, map, game time, reproduction steps, SMAPI log and relevant traces. Remove API keys and private conversation content before sharing.
