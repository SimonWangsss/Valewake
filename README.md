# Stardew Agent Framework

This workspace contains a SMAPI-based, research-oriented AI NPC dialogue system and a future bounded-action agent foundation.

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

`StardewAgentFramework/`

Current features:

- Loads as a SMAPI mod.
- Reads/writes `config.json`.
- Logs game launch/save/day events.
- Provides a console command `agent_state`.
- Provides continuous right-click AI dialogue with Abigail while preserving the vanilla opening interaction.
- Keeps a console fallback command `agent_chat <message>` when the player is near Abigail.
- Captures a full diagnostic Agent snapshot and a separate bounded NPC-visible snapshot.
- Sends chat requests to the independent Stardew backend at `http://127.0.0.1:8010/chat`.
- Starts the bundled local backend automatically when SMAPI loads the mod.
- Applies validated relationship effects to vanilla friendship plus Agent rapport/trust.
- Renders validated NPC emotion through Stardew's native portrait commands.

No autonomous actions are implemented yet. This is intentional: first verify SMAPI loading and state perception.

## Independent backend

Path:

`backend/`

This backend is independent from the earlier Unity/Yu Gong prototype. It has its own FastAPI app, package namespace, memory file, RAG folder, and Abigail/Stardew prompt policy.

Current backend features:

- `/chat`
- mock or OpenAI-compatible LLM client
- layered semantic and episodic player memory
- hybrid JSONL RAG for Abigail persona, relationships, Stardew lore, and boundaries
- social-context-aware Dialogue Policy
- deterministic offline evaluation and real-game Trace scoring
- dialogue-only capability boundary: action proposals are traced but never executed

## Build

```powershell
cd E:\Codex\ai-npc-3d-persona-memory\projects\StardewAgentFramework\StardewAgentFramework
dotnet build
```

If Stardew Valley is installed and SMAPI's mod build config finds it, the build may copy the mod to your Mods folder automatically. If it does not, copy the build output folder containing `StardewAgentFramework.dll`, `manifest.json`, and `config.json` into:

```text
Stardew Valley\Mods\StardewAgentFramework
```

## In game

After loading a save, open the SMAPI console and run:

```text
agent_state
```

To test chat, start Stardew Valley through SMAPI. The mod checks `/health` and starts
`Backend/StardewAgentBackend.exe` in a hidden process when port 8010 is not already serving the agent backend.
The deployed `Backend/.env` contains the local model-provider configuration and is not included in release ZIP files.

Stand near Abigail and either right-click her for continuous dialogue or run:

```text
agent_chat What should I do today?
```

Expected behavior:

- SMAPI loads `Stardew Agent Framework`.
- The bundled backend starts automatically and stops with the game when it was started by this mod.
- The console logs save/day events.
- `agent_state` prints a current game-state snapshot.
- `agent_chat` sends Abigail, player input, and the current game snapshot to the backend, then shows Abigail's response in a Stardew dialogue box.
