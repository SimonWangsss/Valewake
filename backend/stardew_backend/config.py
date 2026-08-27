import os
from dataclasses import dataclass
from pathlib import Path


def load_dotenv(path: Path) -> None:
    if not path.exists():
        return
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


@dataclass(frozen=True)
class Settings:
    llm_backend: str
    llm_api_base: str
    llm_api_key: str
    llm_model: str
    llm_timeout_seconds: int
    rag_dir: Path
    memory_path: Path
    trace_path: Path
    top_k_rag: int
    top_k_memory: int
    max_episodes_per_session: int
    llm_thinking_mode: str = "disabled"
    llm_max_tokens: int = 700
    llm_json_mode: bool = True
    persona_path: Path = Path("./data/personas/stardew_npcs.json")

    @classmethod
    def from_env(cls) -> "Settings":
        load_dotenv(Path(".env"))
        return cls(
            llm_backend=os.getenv("LLM_BACKEND", "mock").lower(),
            llm_api_base=os.getenv("LLM_API_BASE", "http://127.0.0.1:8000/v1").rstrip("/"),
            llm_api_key=os.getenv("LLM_API_KEY", ""),
            llm_model=os.getenv("LLM_MODEL", "qwen"),
            llm_timeout_seconds=int(os.getenv("LLM_TIMEOUT_SECONDS", "60")),
            llm_thinking_mode=os.getenv("LLM_THINKING_MODE", "disabled").strip().lower(),
            llm_max_tokens=int(os.getenv("LLM_MAX_TOKENS", "700")),
            llm_json_mode=os.getenv("LLM_JSON_MODE", "true").strip().lower() in {"1", "true", "yes", "on"},
            rag_dir=Path(os.getenv("STARDEW_RAG_DIR", "./data/rag/stardew")),
            memory_path=Path(os.getenv("STARDEW_MEMORY_PATH", "./data/memory/player_memory.json")),
            trace_path=Path(os.getenv("STARDEW_TRACE_PATH", "./data/traces/agent_trace.jsonl")),
            top_k_rag=int(os.getenv("STARDEW_TOP_K_RAG", "5")),
            top_k_memory=int(os.getenv("STARDEW_TOP_K_MEMORY", "4")),
            max_episodes_per_session=int(os.getenv("STARDEW_MAX_EPISODES_PER_SESSION", "80")),
            persona_path=Path(os.getenv(
                "STARDEW_PERSONA_PATH",
                "./data/personas/stardew_npcs.json",
            )),
        )
