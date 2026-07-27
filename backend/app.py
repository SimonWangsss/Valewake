from typing import Any, Dict, Optional

from fastapi import FastAPI
from pydantic import BaseModel, Field

from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings


class ChatRequest(BaseModel):
    player_input: str
    game_state: Dict[str, Any] = Field(default_factory=dict)
    session_id: str = "default"
    conversation_history: list[Dict[str, str]] = Field(default_factory=list)
    debug: bool = False


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


app = FastAPI(title="Stardew Agent Backend")
settings = Settings.from_env()
agent = StardewAgent(settings)


@app.get("/health")
def health() -> Dict[str, Any]:
    return {
        "ok": True,
        "project": "stardew_agent_mod",
        "version": "0.7.0",
        "dialogue_system": "v3",
        "memory_schema": 3,
        "lore_chunks": len(agent.rag.chunks),
        "llm_backend": settings.llm_backend,
        "rag_dir": str(settings.rag_dir),
        "memory_path": str(settings.memory_path),
        "trace_path": str(settings.trace_path),
    }


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
