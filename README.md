# Valewake

This workspace contains a SMAPI-based, research-oriented AI NPC dialogue system and bounded farm/mine agents.

## References cloned locally

- `references/ValleyTalk`
- `references/StardewGPT`
- `references/stardew-mcp`
- `references/SMAPI`

Reference analysis:

- `docs/reference_review.md`: Stardew AI mod and SMAPI review.
- `docs/minecraft_agent_reference_review.md`: SecondBrain, AI-Player, Mindcraft/MineCollab, Voyager, and their lessons for the Stardew agent architecture.

The scaffold follows the common SMAPI pattern used by these projects:

- `manifest.json`
- `ModEntry : Mod`
- `Pathoschild.Stardew.ModBuildConfig`
- `config.json`

## Prototype mod

Path:

`Valewake/`

Current features:

- Loads as a SMAPI mod.
- Reads/writes `config.json`.
- Logs game launch/save/day events.
- Provides a console command `agent_state`.
- Provides continuous right-click AI dialogue with every social NPC while preserving the vanilla opening interaction.
- Keeps a console fallback command `agent_chat <message>` near the closest supported NPC.
- Captures a full diagnostic Agent snapshot and a separate bounded NPC-visible snapshot.
- Sends chat requests to the independent Stardew backend at `http://127.0.0.1:8010/chat`.
- Starts the bundled local backend automatically when SMAPI loads the mod.
- Applies validated relationship effects to vanilla friendship plus Agent rapport/trust.
- Renders validated NPC emotion through Stardew's native portrait commands.
- Converts watering, weeding, and bounded tree-chopping requests into typed, locally validated proposals.
- Requires native Yes/No confirmation before creating a persistent NPC job.
- Supports local farm pathfinding, bounded per-tile execution, cross-map dispatch,
  schedule restoration, failure recovery, host authority, and Action Trace.
- Supports typed mine companionship: following across mine levels, nearby defense,
  cursor-selected mining, bounded nearby mining, and resource-priority expeditions.
- Links dialogue `turn_id`, action `proposal_id`, and job/expedition IDs through outcome traces.
- Records save commit/rollback boundaries and supports in-game `agent_mark` annotations.
- Exports curated, anonymized dialogue/action JSONL datasets.

## Independent backend

Path:

`backend/`

This backend is independent from the earlier Unity/Yu Gong prototype. It has its own FastAPI app, package namespace, memory file, RAG folder, and Stardew dialogue policy.

Current backend features:

- `/chat`
- `/trace/event` and `/trace/mark`
- mock or OpenAI-compatible LLM client
- four-layer player memory: conversation, episodic, semantic retrieval, and durable profile
- hybrid JSONL RAG with topic routing, BM25, character n-gram TF-IDF vectors, and parent context
- 34 curated base-game NPC profiles plus a bounded fallback for modded social NPCs
- 54 atomic Lore chunks for Abigail's detailed persona, shared Stardew lore, mechanics, and boundaries
- NPC-scoped retrieval that prevents one resident's private Lore from leaking into another resident's prompt
- social-context-aware Dialogue Policy
- deterministic offline evaluation and real-game Trace scoring
- typed watering/weeding proposals with deterministic normalization and evidence grounding
- typed mine-expedition proposals with a deterministic local controller
- same-language validation with a bounded Chinese correction retry

## Build

```powershell
cd E:\Codex\ai-npc-3d-persona-memory\projects\Valewake\Valewake
dotnet build Valewake.csproj
```

For a reproducible release that rebuilds the one-file backend, synchronizes the
tracked persona/RAG data, creates the mod ZIP, and deploys to the configured Mods
folder, run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package_mod.ps1
```

If Stardew Valley is installed and SMAPI's mod build config finds it, the build may copy the mod to your Mods folder automatically. If it does not, copy the build output folder containing `Valewake.dll`, `manifest.json`, and `config.json` into:

```text
Stardew Valley\Mods\Valewake
```

## In game

After loading a save, open the SMAPI console and run:

```text
agent_state
```

To test chat, start Stardew Valley through SMAPI. The mod checks `/health` and starts
`Backend/ValewakeBackend.exe` in a hidden process when port 8010 is not already serving the Valewake backend.
The deployed `Backend/.env` contains the local model-provider configuration and is not included in release ZIP files.

Stand near any social NPC and either right-click them for continuous dialogue or run:

```text
agent_chat What should I do today?
```

For a bounded job, ask an eligible adult NPC with at least two hearts:

```text
Can you help me water the nearby crops?
Can you clear the weeds on my farm?
Can you chop a few mature trees on my farm?
```

The NPC may accept, refuse, or negotiate in character. An accepted proposal
still requires local validation and a Yes/No confirmation. Use `agent_jobs` to
inspect the active queue or `agent_cancel_jobs` to cancel and restore schedules.
See `docs/action_agent_v1_test_plan.md` for the complete manual test matrix.

To test mine companionship, ask an adult NPC with at least four hearts:

```text
陪我下矿。
陪我下矿探险，优先找铁矿。
保护我，帮我打附近的怪。
帮我挖附近的矿石。
```

Accepting `陪我下矿。` now starts one continuous expedition that follows between maps,
mines nearby eligible nodes, and defends against nearby hostile monsters. Press `G`
while the cursor is over or within one tile of a breakable mine rock to make it the
priority target. Use `agent_expedition` for status and `agent_end_expedition` to leave.
See `docs/mine_expedition_test_plan.md` for staged verification.
See `docs/action_catalog.md` for every executable task, its limits, and end conditions.

After any displayed AI turn, annotate it in the SMAPI console:

```text
agent_mark keep 回答自然且符合角色
agent_mark memory_bad 把一次性闲聊写成长期记忆
agent_mark action_good 正确提出并完成任务
```

See `docs/playtest_dataset.md` for save-aware JSONL export.

The current objective metric snapshot and recommended functional benchmark are in
`docs/evaluation_results_0.10.0.md`. Run the action routing and real execution scorers with:

```powershell
cd backend
.\.venv\Scripts\python.exe .\eval\run_action_eval.py
.\.venv\Scripts\python.exe .\eval\score_action_traces.py `
  --farm "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\action_trace.jsonl" `
  --expedition "E:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Valewake\data\traces\expedition_trace.jsonl"
```

Expected behavior:

- SMAPI loads `Valewake`.
- The bundled backend starts automatically and stops with the game when it was started by this mod.
- The console logs save/day events.
- `agent_state` prints a current game-state snapshot.
- `agent_chat` sends the nearest NPC, player input, and bounded perception to the backend, then shows that NPC's response in a Stardew dialogue box.
