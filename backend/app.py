from typing import Any, Dict, Optional

from fastapi import FastAPI
from pydantic import BaseModel, Field

from stardew_backend import providers
from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings, save_llm_config


class ChatRequest(BaseModel):
    player_input: str
    game_state: Dict[str, Any] = Field(default_factory=dict)
    session_id: str = "default"
    conversation_history: list[Dict[str, str]] = Field(default_factory=list)
    debug: bool = False


class LlmConfigRequest(BaseModel):
    provider: str = ""
    api_base: str = ""
    api_key: str = ""
    model: str = ""
    backend: str = "openai"


class ChatResponse(BaseModel):
    reply: str
    emotion: str = "neutral"
    retrieved_lore: list[str]
    retrieved_memory: list[str]
    saved_memories: list[str]
    relationship_effect: Dict[str, Any] = Field(default_factory=dict)
    action_proposal: Optional[Dict[str, Any]] = None
    turn_id: str = ""
    debug_prompt: Optional[str] = None


class MemorySessionRequest(BaseModel):
    session_prefix: str


class TraceEventRequest(BaseModel):
    event: str
    payload: Dict[str, Any] = Field(default_factory=dict)


class TraceMarkRequest(BaseModel):
    turn_id: str
    label: str
    note: str = ""
    tags: list[str] = Field(default_factory=list)


app = FastAPI(title="Valewake Backend")
settings = Settings.from_env()
agent = StardewAgent(settings)


@app.get("/health")
def health() -> Dict[str, Any]:
    return {
        "ok": True,
        "project": "valewake",
        "version": "0.10.0",
        "dialogue_system": "v7-action-alignment",
        "memory_schema": 3,
        "lore_chunks": len(agent.rag.chunks),
        "curated_npc_profiles": agent.personas.curated_count,
        "llm_backend": agent.llm.config.backend,
        "llm_model": agent.llm.config.model,
        "llm_thinking_mode": agent.llm.config.thinking_mode,
        "rag_dir": str(settings.rag_dir),
        "memory_path": str(settings.memory_path),
        "trace_path": str(settings.trace_path),
    }


@app.get("/config")
def get_config() -> Dict[str, Any]:
    provider = providers.provider_by_url(agent.llm.config.api_base)
    return {
        "providers": providers.PROVIDERS,
        "current": {
            "provider": (provider or {}).get("id", ""),
            "backend": agent.llm.config.backend,
            "api_base": agent.llm.config.api_base,
            "model": agent.llm.config.model,
            "has_api_key": bool(agent.llm.config.api_key),
        },
    }


@app.post("/config")
def set_config(request: LlmConfigRequest) -> Dict[str, Any]:
    api_base = (request.api_base or "").strip().rstrip("/")
    backend = (request.backend or "").strip().lower() or "openai"
    model = (request.model or "").strip()
    api_key = (request.api_key or "").strip()

    provider = providers.find_provider((request.provider or "").strip())
    if provider is not None:
        if not api_base:
            api_base = provider["base_url"]
        backend = provider["format"]
    if not api_base:
        return {"ok": False, "error": "missing_api_base"}
    if not api_key:
        return {"ok": False, "error": "missing_api_key"}
    if not model:
        return {"ok": False, "error": "missing_model"}

    agent.update_llm_config(backend, api_base, api_key, model)
    save_llm_config({
        "llm_backend": backend,
        "llm_api_base": api_base,
        "llm_api_key": api_key,
        "llm_model": model,
    })
    return {"ok": True, "backend": backend, "api_base": api_base, "model": model}


@app.post("/chat", response_model=ChatResponse)
def chat(request: ChatRequest) -> ChatResponse:
    result = agent.chat(
        player_input=request.player_input,
        game_state=request.game_state,
        session_id=request.session_id,
        conversation_history=request.conversation_history,
        debug=request.debug,
    )
    return ChatResponse(**result)


@app.post("/memory/commit")
def commit_memory(request: MemorySessionRequest) -> Dict[str, Any]:
    agent.memory.commit_session(request.session_prefix)
    return {"ok": True, "operation": "commit", "session_prefix": request.session_prefix}


@app.post("/memory/rollback")
def rollback_memory(request: MemorySessionRequest) -> Dict[str, Any]:
    agent.memory.rollback_session(request.session_prefix)
    return {"ok": True, "operation": "rollback", "session_prefix": request.session_prefix}


@app.post("/trace/event")
def trace_event(request: TraceEventRequest) -> Dict[str, Any]:
    event_id = agent.trace.append_event(request.event, request.payload)
    return {"ok": True, "event_id": event_id}


@app.post("/trace/mark")
def trace_mark(request: TraceMarkRequest) -> Dict[str, Any]:
    allowed = {
        "keep", "reject", "boundary", "memory_good", "memory_bad",
        "action_good", "action_bad",
    }
    label = request.label.strip().lower()
    if label not in allowed:
        return {"ok": False, "error": "unsupported_label", "allowed": sorted(allowed)}
    annotation_id = agent.trace.append_annotation(
        request.turn_id.strip(),
        label,
        request.note.strip(),
        [tag.strip() for tag in request.tags if tag.strip()][:12],
    )
    return {"ok": True, "annotation_id": annotation_id}
