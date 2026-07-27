# Valewake Architecture

## Project boundary

This project is independent from the earlier Unity/Yu Gong prototype. It has its own SMAPI mod, FastAPI backend, prompts, RAG data, memory store, traces, and configuration.

ValleyTalk and StardewGPT are references for Stardew dialogue lifecycle and UI behavior. No ValleyTalk source module is embedded in this framework. The framework's distinctive layer is the controlled path from dialogue to durable relationship state and, later, bounded actions.

## Runtime flow

```text
SMAPI interaction
  -> vanilla first dialogue (normal right-click path)
  -> continuous text conversation session
  -> NPC Perception Builder
  -> FastAPI /chat
  -> Lore RAG + player/NPC memory
  -> LLM structured generation
       reply
       emotion
       relationship_effect proposal
       memory candidates
       optional action_proposal
  -> backend validation + JSONL trace
  -> SMAPI Relationship Policy
  -> vanilla friendship update + Agent Relationship update
  -> dialogue box + HUD feedback
```

### Runtime Host

`BackendProcessManager.cs` first checks the loopback `/health` endpoint. If no compatible service is
running and `AutoStartBackend` is enabled, it starts the bundled single-file backend with a hidden
window and waits for readiness. Chat requests repeat this readiness check, so an early startup delay
does not permanently disable dialogue. The manager only terminates a backend process it started itself.

The backend runs with `Mods/Valewake/Backend` as its working directory. This keeps `.env`,
RAG data, durable memory, and traces together while leaving credentials and player data out of release ZIPs.

## Components

### Game Adapter

`ModEntry.cs` owns SMAPI lifecycle, main-thread dispatch, proximity checks, backend calls, dialogue display, and future dialogue hooks.

For every social NPC, a normal action-button interaction keeps the original first dialogue and its vanilla side effects. When that dialogue closes, Valewake opens its own text-entry menu. Each AI reply uses the standard speaker DialogueBox and returns to text entry until the player ends the session. Events, festivals, sleeping NPCs, held-item interactions, and non-social characters remain on the vanilla path.

`PerceptionSnapshot.cs` is the full farmhand/agent perception model. It remains available for diagnostics and future autonomous work.

`NpcPerceptionSnapshot.cs` is the bounded ordinary-NPC view used by every Valewake conversation. It includes time, weather, current location, visible held item, current relationship, and nearby facts. It intentionally excludes global farm totals, money, complete inventory, and hidden quest state.

### Dialogue Kernel

The backend retrieves local lore and session-scoped memories, builds the character prompt, and asks the LLM for strict JSON. Free-form model output is parsed with a neutral fallback so malformed output cannot change relationship state.

Dialogue emotion is a separate validated field (`neutral`, `happy`, `sad`, `angry`, or `affectionate`).
The SMAPI adapter maps it to Stardew's native portrait commands (`$h`, `$s`, `$a`, `$l`) only when
rendering the dialogue. Raw model text cannot directly inject portrait commands into conversation history.

### Memory

Memory is keyed by `save-folder:NPC`, preventing memories from leaking between saves or characters. Rule-based extraction handles simple facts, while LLM memory candidates require an allowed type, sufficient confidence, and an evidence quote that occurs in the player's input.

### Persona Registry and Lore Scope

`data/personas/stardew_npcs.json` contains compact authoritative profiles for 34 base-game social NPCs. A profile defines age group, identity, personality, interests, relationships, speech style, and hard boundaries. Modded social NPCs without a curated entry use a conservative generic resident profile instead of borrowing a base-game character.

The persona profile is always injected because identity is not optional retrieval context. Detailed Lore remains retrieval-based. Character-specific chunks are filtered by the active NPC before ranking, while world, mechanics, perception, and action-boundary chunks are shared. Abigail currently has the richest detailed Lore; the other residents have complete baseline profiles and can receive deeper atomic Lore incrementally.

Working memory is held by the SMAPI session as the most recent configurable number of player/NPC messages. It is sent to the backend on each turn and discarded when the conversation ends. Selected durable memories remain in the backend store.

The v2 store has two durable layers. Semantic memory keeps selected player facts with evidence, confidence,
importance, reinforcement count, access count, and game-day metadata. Episodic memory keeps a bounded set of
recent player/NPC exchanges per save and NPC. It supports cross-session repetition detection, elapsed-game-day
context, and recent emotional continuity without placing the entire raw history in every prompt.

### Social Context and Dialogue Policy

Before generation, `DialoguePolicy` combines episodic similarity, elapsed game days, vanilla relationship state,
and general risk signals. It emits a response stance such as `respond_naturally`, `acknowledge_repetition`,
`challenge_repetition`, `set_gentle_boundary`, `reconnect_before_topic`, or `refuse_in_character`.
The stance constrains the response goal rather than supplying exact dialogue. It can also block inappropriate
positive relationship gains, while the existing SMAPI relationship authority remains the only component allowed
to change vanilla friendship.

### Relationship Authority

The LLM cannot write friendship points. It proposes `valence`, `intensity`, `confidence`, `reason`, and `evidence`.

`RelationshipManager.cs` is the authority layer. It rejects neutral, low-confidence, unsupported, malformed, or daily-limit-exceeding proposals. Accepted effects update:

- Stardew's vanilla friendship points, for compatibility with heart events and relationship progression.
- Agent rapport, a bounded conversational affinity value.
- Agent trust, changed only by strong relationship moments.
- Recent mood and the last accepted reason.

All values are stored through SMAPI save data and are isolated per save.

### Trace and Evaluation

Every backend turn is appended to `backend/data/traces/agent_trace.jsonl` with perception, input, retrieved lore, retrieved memory, reply, relationship proposal, memory writes, and any action proposal. This is the source data for later regression evaluation.

`backend/eval/run_dialogue_eval.py` runs deterministic policy, retrieval, memory migration, and end-to-end mock
cases without API cost. `backend/eval/score_traces.py` scores real turns for structural and policy invariants.
Model-judged character-quality evaluation remains a later layer and should be reported separately from these
deterministic checks.

### Future Action Authority

The response protocol already reserves `action_proposal`, but Phase 4 does not execute it. Future actions use the same authority pattern:

```text
LLM proposes intent
  -> Action Policy checks relationship, consent, schedule, location, budget, multiplayer, and allowlist
  -> player confirmation when needed
  -> deterministic SMAPI executor performs the action
  -> result and failure reason are traced
```

Planned low-risk executors are follow, water nearby crops, harvest nearby mature crops, and purchase a specifically confirmed item within a fixed budget.

## Current testing surface

- `agent_state`: prints the full Phase 2 diagnostic perception.
- `agent_chat <message>`: chats with the nearest social NPC, using bounded NPC perception.
- Normal right-click: preserves the vanilla first line, then starts continuous typed AI dialogue.
- Enter or the confirm icon sends text; Escape or the cancel icon ends the AI session.
- Positive and negative high-confidence exchanges can change vanilla friendship within configured daily limits.
- Relationship feedback appears as a HUD message.
- Action proposals are logged and traced but never executed.
