# Valewake Backend

## Model providers

Valewake uses an OpenAI-compatible `chat/completions` boundary. Ready-to-edit
profiles are in `providers/` for direct DeepSeek, Alibaba Cloud Model Studio
(Qwen), Gemini's OpenAI-compatible endpoint, and OpenRouter. Copy the selected
profile values into `.env`, replace only `LLM_API_KEY`, and restart the backend.

For real-time NPC dialogue, keep `LLM_THINKING_MODE=disabled`. DeepSeek enables
thinking by default unless this is sent explicitly. `LLM_JSON_MODE=true` requests
the structured response used by the dialogue parser; set it to `false` only when
the chosen provider/model rejects `response_format`.

Every real call now records latency and token usage in `agent_trace.jsonl` under
`llm_metrics`, without recording the API key.

Independent FastAPI backend for `Valewake`.

It is intentionally separate from the earlier Unity/Yu Gong prototype. It has its own package, memory file, RAG folder, and prompts.

## Features

- `/chat` endpoint for the SMAPI mod.
- Mock or OpenAI-compatible LLM client.
- Per-save and per-NPC player memory in JSON schema v3.
- Curated persona profiles for 34 base-game social NPCs and a bounded fallback for modded NPCs.
- NPC-scoped Lore retrieval that prevents cross-character persona leakage.
- Conversation, episodic, semantic-retrieval, and durable-profile memory layers.
- Semantic deduplication, contradiction supersession, and grounded candidate validation.
- Social-context analysis for repetition, time gaps, intimacy mismatch, and boundary pressure.
- Topic-routed BM25 + character n-gram TF-IDF vector retrieval.
- 54 atomic Lore chunks linked to foundation chunks through `parent_id`.
- A deterministic Dialogue Policy layer before generation and relationship application.
- Structured NPC replies with relationship-effect and future action proposals.
- Short-term continuous-dialogue history supplied by the SMAPI client.
- Append-only JSONL decision traces.
- Offline policy/retrieval/memory evaluation and a real-trace structural scorer.

## Setup

```powershell
cd E:\Codex\ai-npc-3d-persona-memory\projects\Valewake\backend
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
Copy-Item .env.example .env
```

Default `.env` uses `LLM_BACKEND=mock`, so it works without a model server.

For an OpenAI-compatible server:

```text
LLM_BACKEND=openai
LLM_API_BASE=http://127.0.0.1:8000/v1
LLM_API_KEY=your_key_if_needed
LLM_MODEL=qwen
```

For DeepSeek's official API:

```powershell
Copy-Item .env.deepseek.example .env
```

Then edit `.env` and set your real key:

```text
LLM_BACKEND=openai
LLM_API_BASE=https://api.deepseek.com
LLM_API_KEY=your_deepseek_api_key
LLM_MODEL=deepseek-v4-flash
```

Use `deepseek-v4-flash` for the first mod test. You can switch to `deepseek-v4-pro` later if you want stronger responses.

The backend never writes Stardew save data. It only proposes a relationship effect. The SMAPI-side relationship policy validates confidence, evidence, intensity, and daily limits before applying any vanilla friendship change.

## Run

Normal installed-mod usage does not require this command. The backend is packaged as
`Mods/Valewake/Backend/ValewakeBackend.exe`; the SMAPI mod starts it automatically,
checks `/health`, retries before chat, and stops only the process it owns when the game exits.

Use the command below only for backend development:

```powershell
.\.venv\Scripts\python.exe -m uvicorn app:app --host 127.0.0.1 --port 8010
```

Runtime data is written under `backend/data` by default:

- `memory/player_memory.json`: durable player memories, isolated by save and NPC session ID.
- `traces/agent_trace.jsonl`: one inspectable record per dialogue turn.

## Evaluation

Run the deterministic Dialogue System v2 suite without an API call:

```powershell
.\.venv\Scripts\python.exe .\eval\run_dialogue_eval.py
```

Run the focused v3 core tests for query routing, contradiction handling,
durable profiles, candidate rejection, and Chinese policy boundaries:

```powershell
.\.venv\Scripts\python.exe -m unittest .\eval\test_dialogue_core_v3.py -v
```

Score real gameplay traces for role leakage, grounded relationship effects, emotion validity, and policy enforcement:

```powershell
.\.venv\Scripts\python.exe .\eval\score_traces.py
```

Run the 132-case golden suite. By default this executes 84 deterministic
Lore/Policy/Memory checks and skips the 48 model-generation cases:

```powershell
.\.venv\Scripts\python.exe .\eval\run_golden_eval.py
```

Add `--live` to call the configured model for the 48 generation cases. This
can incur API usage:

```powershell
.\.venv\Scripts\python.exe .\eval\run_golden_eval.py --live
```

The detailed report is written to
`data/test_runs/golden_eval_latest.json`. See
`../docs/evaluation_guide.md` for the schema, manual rubric, and complete test
workflow.

Each run also writes a timestamped report. Compare two reports produced from
the same test-set hash:

```powershell
.\.venv\Scripts\python.exe .\eval\compare_eval_reports.py <baseline.json> <candidate.json>
```

## Standalone multi-NPC test window

Double-click `run_valewake_test_chat.cmd` in the project root, or run:

```powershell
.\.venv\Scripts\python.exe .\tools\standalone_chat.py
```

It uses the same Agent/Lore/Memory/Policy pipeline without starting Stardew
Valley or FastAPI. Select an NPC in the context panel. Test memory and traces
are isolated under `data/test_runs`.

## Test

```powershell
$body = @{
  player_input = "What should I do today?"
  session_id = "test:Abigail"
  debug = $true
  game_state = @{
    source = "stardew_valley_smapi"
    npc = @{ name = "Abigail"; display_name = "Abigail" }
    snapshot = @{
      season = "spring"
      dayOfMonth = 3
      timeOfDay = 900
      isRaining = $true
      stamina = 250
      locationName = "Town"
    }
  }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod -Method Post http://127.0.0.1:8010/chat -ContentType "application/json" -Body $body
```
