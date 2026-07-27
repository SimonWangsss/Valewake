# Stardew AI Mod Reference Review

Date: 2026-07-13

This review covers the local reference projects under `references/`:

- `ValleyTalk`
- `StardewGPT`
- `stardew-mcp`
- `SMAPI`

The goal is not to clone an existing "AI dialogue NPC" mod. The target project is an agentic Stardew Valley farmhand framework: perception, dialogue, memory, planning, safety-gated actions, and trace/eval.

## Short Answer

Other LLM Stardew mods do contain pieces similar to the proposed modules, but usually not as cleanly separated agent layers.

| Module | Seen in references? | Notes |
| --- | --- | --- |
| Perception Builder | Yes, partially | `ValleyTalk` builds natural-language prompt context from game state. `stardew-mcp` serializes a large JSON game state. Neither is exactly the lightweight agent interface we want. |
| Evaluation & Trace | Partial | `ValleyTalk` stores dialogue/event history. `StardewGPT` logs requests in debug. `stardew-mcp` logs tool calls and command results. None provides a dedicated eval harness for NPC quality and safety. |
| Dialogue Policy | Partial | `ValleyTalk` has prompt instructions, dialogue cleanup, content-pack permission checks, and emotion/format validation. `StardewGPT` has minimal response validation. No explicit intent/risk/policy layer. |
| Player Memory | Partial | `ValleyTalk` keeps event/dialogue history and reads friendship/marriage state. `StardewGPT` only keeps recent in-session conversation. No project has the preference/goal memory model we want. |
| Lore RAG | Weak/implicit | `ValleyTalk` uses game summary, NPC bios, sample dialogue, and event history as context, but not retrieval over chunks. `StardewGPT` has no RAG. |
| Action Tools | Yes | `stardew-mcp` has a large command executor and WebSocket tool bridge. This is the closest reference for action execution, but its tool surface is much too broad for a safe NPC farmhand MVP. |

Recommendation: build our own clean modules, borrowing implementation patterns from these projects:

1. Use `stardew-mcp` as the main reference for structured perception and action command envelopes.
2. Use `ValleyTalk` as the main reference for dialogue hooks, prompt context, content packs, event history, and NPC persona material.
3. Use `StardewGPT` as the simplest reference for an in-game typed chat loop and OpenAI-style HTTP call.
4. Use SMAPI docs/source patterns for lifecycle, config, save data, and game-thread constraints.

## Reference Project Findings

### ValleyTalk

Primary role: full-featured AI dialogue replacement/augmentation for Stardew NPCs.

Important files:

- `src/ModEntry.cs`
- `src/Prompts.cs`
- `src/GameSummaryBuilder.cs`
- `src/Character.cs`
- `src/EventHistoryReader.cs`
- `src/Patches/NPC_CurrentDialogue_Patch.cs`
- `src/Patches/NPC_TryToRetrieveDialogue_Patch.cs`
- `src/Patches/NPC_CheckForNewCurrentDialogue_Patch.cs`
- `src/Patches/NPC_PushTemporaryDialogue_Patch.cs`
- `ContentPack/assets/Prompts.json`
- `ContentPack/assets/GameSummary.json`
- `ContentPack/assets/bio/*.json`

What it does well:

- Supports many LLM providers through a provider map: OpenAI, OpenAI-compatible, Anthropic, Gemini, Mistral, DeepSeek, VolcEngine, LlamaCpp, etc.
- Uses Harmony patches to intercept normal NPC dialogue generation instead of requiring a custom-only interaction flow.
- Builds rich prompt context from game state: date/time, weather, nearby NPCs, location, farm contents, crops, animals, buildings, friendship/marriage status, spouse/children, gifts, recent events, and current conversation.
- Separates stable game context and NPC context through content assets like game summaries, bios, relationships, traits, sample dialogue, and prompt text.
- Records dialogue/event history and handles overheard nearby dialogue, which is relevant for believable NPC memory.
- Has content-pack permission handling for AI use. This matters because AI dialogue generation can ingest modded author content.
- Exposes an API surface for interop/prompt overrides.

How it hooks dialogue:

- `ModEntry.Entry` initializes config, LLM provider, content packs, text input, and Harmony patches.
- `NPC.tryToRetrieveDialogue` can be replaced with a generated dialogue marker.
- `NPC.checkForNewCurrentDialogue` can push a generated dialogue marker in selected contexts.
- `_PushTemporaryDialogue` can wrap vanilla temporary dialogue with a generation tag plus the original line.
- `NPC.CurrentDialogue` getter detects when the game is drawing dialogue. If the next line is the generation tag, it clears/pops the placeholder, starts async generation, and temporarily shows a fallback/thinking state.

API and prompt handling:

- The mod constructs prompts internally rather than sending a clean JSON perception object.
- `Prompts.cs` composes system prompt, game constant context, NPC constant context, core prompt, command, response start, and instructions.
- `GameSummaryBuilder.cs` loads content assets and builds a natural-language Stardew summary.
- NPC bios and sample dialogue are loaded from content-pack JSON.

Useful ideas to borrow:

- Content-pack style persona/lore assets.
- Prompt sections with explicit responsibilities.
- Dialogue hook strategy with generated dialogue markers.
- Event/dialogue history storage.
- Nearby NPC awareness.
- Permission-aware handling of third-party content.
- Response cleanup and validation before inserting text into the game dialogue system.

What not to copy directly:

- Do not make prompt construction the only perception interface. For our project, keep a structured `PerceptionSnapshot` JSON first, then derive prompt text from it.
- Do not patch every vanilla dialogue path in the first version. Start with one explicit farmhand interaction or hotkey/dialogue entry, then expand.
- Do not couple history, prompt rendering, and LLM invocation too tightly. We need trace/eval and action planning, so intermediate data should stay inspectable.
- Do not ingest modded content without an explicit policy.

### StardewGPT

Primary role: minimal AI chat with nearby NPC.

Important files:

- `StardewGPT/StardewGPT.cs`
- `StardewGPT/GptApi.cs`
- `StardewGPT/GPTInputMenu.cs`
- `StardewGPT/GptWaitingMenu.cs`

What it does well:

- Very small vertical slice for typed chat.
- Uses `Input.ButtonPressed` and `Context.IsWorldReady` to start only after a save is loaded.
- Finds a nearby NPC, faces them toward the player, gets a vanilla greeting, and opens a dialogue box.
- Chains dialogue completion into a custom input menu.
- Keeps a short conversation history.
- Builds a minimal system message from NPC name, farmer name, personality fields, relationship, date, and time.
- Uses `HttpClient` to call the OpenAI chat completions endpoint.

How it hooks dialogue:

- It does not patch vanilla NPC dialogue.
- It starts a custom chat loop when the player presses middle mouse near an NPC.
- It uses `Game1.activeClickableMenu = new DialogueBox(...)` to display output and `Dialogue.onFinish = showInputMenu` to request the next player input.

API handling:

- `GptApi` uses `HttpClient`.
- API key is read from `OPENAI_API_KEY`.
- Request is a standard chat-completions JSON body.
- Response is parsed from `choices[0].message.content`.

Useful ideas to borrow:

- This is the best low-risk first implementation pattern for our Phase 1 chat slice.
- Custom input menu + dialogue box loop is simpler and safer than Harmony patching at the beginning.
- Recent conversation limit prevents runaway prompt size.
- A "thinking" menu is useful for async calls.

What not to copy directly:

- Do not hard-code OpenAI only. Our backend should be OpenAI-compatible and configurable.
- Do not put API keys in the mod when a local backend can own model credentials.
- Do not rely only on personality strings from vanilla NPC fields; our farmhand needs explicit role/persona and agent state.
- Do not skip trace logging.

### stardew-mcp

Primary role: WebSocket bridge plus autonomous control tools for Stardew Valley.

Important files:

- `mod/StardewMCP/ModEntry.cs`
- `mod/StardewMCP/GameStateSerializer.cs`
- `mod/StardewMCP/CommandExecutor.cs`
- `mod/StardewMCP/WebSocketServer.cs`
- `mod/StardewMCP/Pathfinder.cs`
- `mcp-server/main.go`
- `mcp-server/copilot_agent.go`

What it does well:

- Clean separation between SMAPI mod, serialized game state, command executor, and external agent process.
- Broadcasts game state over WebSocket.
- Supports explicit `get_state` and `command` messages.
- Serializes a broad game state: player, time, world, map, surroundings, quests, relationships, skills, inventory, nearby tiles/objects/terrain/NPCs/monsters/buildings/animals, warps, and tile-in-front.
- Includes an ASCII map for nearby surroundings.
- Uses command IDs and command responses.
- Queues commands and processes them on game ticks, which is important because Stardew actions must happen in the game loop.
- Tracks movement/pathfinding state and returns command results.

How it abstracts game state:

- `GameStateSerializer.GetGameState()` returns a structured object with nested state classes.
- A fixed scan radius around the player is used for local awareness.
- It snapshots mutable collections with `.ToList()` in many places to reduce concurrent modification crashes.
- It includes both raw local details and higher-level flags like passability, required tools, readiness for harvest, and minutes until ready.

How it abstracts tools/actions:

- The WebSocket protocol has message types like `command`, `get_state`, and `ping`.
- Commands have `id`, `action`, and `params`.
- Responses have `id`, `success`, `message`, and optional `data`.
- `CommandExecutor` maps action names to methods and handles async actions like movement/tool use over multiple ticks.

Useful ideas to borrow:

- Structured JSON perception.
- Command envelope: `id`, `action`, `params`.
- Command result envelope: `id`, `success`, `message`, `data`.
- Game-thread command queue.
- Separate safety/validation layer before execution.
- Tile-in-front and nearby summaries for grounded actions.
- Action verification after execution.

What not to copy directly:

- Do not expose 40+ actions in the first release.
- Do not include cheat tools in the farmhand agent path.
- Do not let the LLM call raw high-risk tools like buy/sell/gift/trash/warp without player confirmation and policy checks.
- Do not stream huge raw maps to every chat request. For dialogue/planning, summarize first and include detailed nearby state only when needed.
- Do not start with a fully autonomous loop. Use `Observe -> Suggest -> Confirm -> Act -> Trace` first.

### SMAPI

Primary role: mod lifecycle, packaging, APIs, and best practices.

Relevant patterns confirmed in current code and references:

- `Entry(IModHelper helper)` is the main mod entry point.
- `helper.ReadConfig<T>()` and `helper.WriteConfig(...)` handle mod config.
- `helper.ConsoleCommands.Add(...)` provides debug commands like `agent_state`.
- `Context.IsWorldReady` should gate logic that needs a loaded save.
- Useful lifecycle events include:
  - `GameLaunched`
  - `SaveLoaded`
  - `DayStarted`
  - `UpdateTicked`
  - `OneSecondUpdateTicked`
  - `ReturnedToTitle`
- Content events like `AssetRequested` and `AssetsInvalidated` support content-pack style data.
- Save-specific data can be stored through `Helper.Data.ReadSaveData<T>()` and `Helper.Data.WriteSaveData(...)`.
- Multiplayer and host/client behavior need explicit policy. `ValleyTalk` has special handling for multiplayer history files.

Useful ideas to borrow:

- Keep long-running or repeated action processing in update ticks.
- Keep slow LLM/backend calls asynchronous and never block the game loop.
- Use config for backend URL, debug logging, enabled mode, and keybindings.
- Use save data for player memory and trace pointers.
- Use console commands heavily during development.

## Comparison Against Proposed Agent Modules

### 1. Perception Builder

This should be the first priority.

References show two styles:

- `ValleyTalk`: perception as natural-language prompt sections.
- `stardew-mcp`: perception as detailed structured JSON.

Our version should combine them but stay smaller:

```json
{
  "schema_version": "0.1",
  "mode": "dialogue_or_planning",
  "player": {},
  "time": {},
  "world": {},
  "location": {},
  "farm_summary": {},
  "inventory_summary": {},
  "nearby": {},
  "relationships": {},
  "current_goal": null,
  "risk_flags": []
}
```

Do not send everything all the time. Use tiers:

- `compact`: chat and daily advice.
- `nearby_detail`: local action suggestions.
- `full_debug`: console/eval only.

Immediate fields to add after the current `GameStateSnapshot`:

- `time`: season, day, time, day_of_week, minutes_until_2am.
- `world`: weather, is_raining, is_storm, is_outdoors.
- `location`: name, display_name, is_farm, is_mine, is_farmhouse.
- `farm_summary`: crops_need_watering, mature_crops, dead_crops, animals_need_pet, machines_ready.
- `inventory_summary`: tools, seeds, food, sellables, empty_slots.
- `nearby`: nearby_npcs, tile_in_front, interactables, crops_need_watering_nearby.
- `risk_flags`: low_energy, late_night, inventory_full, hostile_nearby, tool_missing.

### 2. Evaluation & Trace

This should be built early because it is content-stable.

References have logs/history, but not a dedicated eval layer. Our trace should be explicit:

```json
{
  "trace_id": "trace_20260713_0001",
  "turn_id": "turn_001",
  "type": "chat",
  "player_input": "...",
  "perception_mode": "compact",
  "perception_summary": "...",
  "policy_result": {},
  "retrieved_lore": [],
  "retrieved_memory": [],
  "llm_request_summary": {},
  "reply": "...",
  "memory_written": [],
  "action_intents": [],
  "safety_decisions": [],
  "execution_results": []
}
```

For Stardew-specific eval, start with 30 cases:

- Rainy day: should not advise watering outdoor crops.
- Low stamina: should avoid expensive work.
- Late night: should suggest returning home.
- Inventory full: should not suggest collecting more items before managing inventory.
- Player preference: money-first vs relaxed/social play should change advice.
- Tool missing: should not suggest watering if no watering can is available.
- Mine danger: should warn on low health.
- Dialogue persona: farmhand should sound like a helper, not a detached narrator.

### 3. Dialogue Policy

References mostly use prompt instructions and cleanup. We should add a small explicit policy layer before LLM response generation.

Initial intent/risk labels:

- `farm_advice`
- `npc_chat`
- `memory_recall`
- `preference_update`
- `daily_plan`
- `action_request`
- `out_of_world`
- `prompt_injection`
- `unsafe_game_action`
- `unknown`

Policy output:

```json
{
  "intent": "daily_plan",
  "risk": "low",
  "allowed_response_modes": ["reply", "plan"],
  "requires_confirmation": false,
  "blocked_reason": null
}
```

### 4. Player Memory

References store game and dialogue history, but they do not provide the memory categories we want for an agentic farmhand.

Start light:

- player name / farm name
- money priority
- crop preference
- risk tolerance
- preferred bedtime
- disliked tasks
- active goals
- important choices

Use SMAPI save data for per-save memory on the mod side or backend JSON keyed by save/player ID. Backend memory is easier to inspect and reuse with eval; SMAPI save data is safer for mod-local state. For MVP, store in backend JSON and include a clear export path later.

### 5. Lore RAG

Existing LLM dialogue mods rely more on static prompt context than retrieval. That is fine for vanilla NPC chat, but our farmhand needs domain knowledge about mechanics, player preferences, and current agent capability.

Do not build a heavy vector pipeline yet. Start with JSONL chunks and simple keyword/scored retrieval:

- `stardew_mechanics`: seasons, crops, weather, time, stamina.
- `agent_capabilities`: what the farmhand can/cannot do.
- `safety_rules`: high-risk action constraints.
- `npc_persona`: farmhand tone and boundaries.

Add chunks only when trace/eval reveals a gap.

## Recommended Architecture for Our Mod

### Mod-side modules

```text
StardewAgentFramework/
├── Perception/
│   ├── GameStateSnapshot.cs
│   ├── PerceptionBuilder.cs
│   ├── FarmSnapshotBuilder.cs
│   ├── InventorySnapshotBuilder.cs
│   └── NearbySnapshotBuilder.cs
├── Backend/
│   ├── AgentBackendClient.cs
│   ├── ChatRequest.cs
│   ├── ChatResponse.cs
│   ├── PlanRequest.cs
│   └── PlanResponse.cs
├── Dialogue/
│   ├── AgentDialogueController.cs
│   └── AgentInputMenu.cs
├── Actions/
│   ├── AgentActionIntent.cs
│   ├── SafetyPolicy.cs
│   ├── ActionExecutor.cs
│   └── ActionResult.cs
└── Debug/
    └── AgentConsoleCommands.cs
```

### Backend modules

```text
backend/
├── app.py
├── agent/
│   ├── chat.py
│   ├── planner.py
│   ├── policy.py
│   ├── memory.py
│   ├── retrieval.py
│   └── traces.py
└── data/
    ├── memory/
    ├── rag/
    └── traces/
```

## Concrete Borrowing Plan

### API calling

Borrow the shape from `StardewGPT`, but route to our backend instead of OpenAI directly.

Recommended first mod endpoint:

```http
POST http://127.0.0.1:8010/chat
```

Request:

```json
{
  "player_id": "save_or_player_id",
  "npc_id": "ai_farmhand",
  "message": "What should I do today?",
  "game_state": {},
  "conversation_context": []
}
```

Response:

```json
{
  "reply": "It's raining, so skip watering outside. With your energy still high, mining or fishing would be a good use of the day.",
  "trace_id": "trace_...",
  "memory_updates": [],
  "suggested_actions": []
}
```

### Dialogue hook

Use a staged approach:

1. First implementation: custom keybind or console command opens a farmhand chat menu. Borrow the simple loop from `StardewGPT`.
2. Second implementation: interact with a specific NPC/farmhand actor.
3. Third implementation: consider Harmony patching like `ValleyTalk` only if we need to replace vanilla NPC dialogue.

Avoid broad Harmony patches early.

### State采集

Borrow from:

- Current `GameStateSnapshot.cs` for existing baseline.
- `ValleyTalk.Prompts` for what matters in dialogue.
- `stardew-mcp.GameStateSerializer` for structured nearby/world/action fields.

First target should be a compact state object, not a full raw dump.

### Tools/actions

Borrow the command envelope from `stardew-mcp`, not the full action set.

Initial action schema:

```json
{
  "id": "act_001",
  "action": "follow_player",
  "params": {},
  "requires_confirmation": true
}
```

Initial allowlist:

- `follow_player`
- `stop_following`
- `show_daily_plan`
- `water_nearby_crops` only after confirmation
- `harvest_nearby_mature_crops` only after confirmation

Explicitly block:

- buy/sell/trash/gift
- warp
- cheat commands
- inventory destruction
- spending money
- relationship-affecting actions
- sleeping
- entering dangerous locations autonomously

## Risk Points

- Dialogue hook instability: Harmony patches can break across game versions or conflict with other mods. Use explicit interaction first.
- Blocking the game loop: LLM calls must be async and UI should show a waiting state.
- Prompt bloat: raw nearby state can become huge. Use compact summaries by default.
- Unsafe actions: even "simple" actions can consume stamina, destroy objects, spend money, or alter relationships.
- Multiplayer: memory and actions need host/client rules. MVP should probably run actions only for single-player or host.
- Modded content rights: do not feed third-party mod content to AI unless allowed.
- Save corruption/annoyance: action execution must avoid irreversible operations at first.
- Secret leakage: API keys should live in backend environment, not in the SMAPI mod config.
- Evaluation blind spots: without trace/eval, it will be hard to tell whether bad answers come from perception, prompt, memory, retrieval, or model behavior.

## Next Implementation Steps

1. Keep current `agent_state` command and verify it in-game.
2. Add `PerceptionBuilder` that outputs a compact JSON state.
3. Add `AgentBackendClient` and `agent_chat` console command to POST state + message to backend.
4. Add trace writing on the backend for every chat call.
5. Add a minimal in-game chat UI/keybind using the `StardewGPT` pattern.
6. Add daily plan endpoint after chat is stable.
7. Add player preference memory.
8. Add action intent schema without execution.
9. Add safety policy and only then implement the first low-risk action.

