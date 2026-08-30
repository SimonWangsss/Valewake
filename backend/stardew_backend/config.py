import json
import os
from dataclasses import dataclass
from pathlib import Path

CONFIG_PATH = Path("llm_config.json")

LLM_CONFIG_KEYS = (
    "llm_backend",
    "llm_api_base",
    "llm_api_key",
    "llm_model",
    "llm_thinking_mode",
    "llm_max_tokens",
    "llm_json_mode",
)


def load_dotenv(path: Path) -> None:
    if not path.exists():
        return
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


def load_llm_config() -> dict:
    """Read the runtime LLM config written by the in-game menu (overrides .env)."""
    if not CONFIG_PATH.exists():
        return {}
    try:
        data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        return {k: v for k, v in data.items() if k in LLM_CONFIG_KEYS} if isinstance(data, dict) else {}
    except (json.JSONDecodeError, OSError):
        return {}


def save_llm_config(config: dict) -> None:
    payload = {k: v for k, v in config.items() if k in LLM_CONFIG_KEYS}
    CONFIG_PATH.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


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
        file_config = load_llm_config()
        return cls(
            llm_backend=str(file_config.get("llm_backend", os.getenv("LLM_BACKEND", "mock"))).lower(),
            llm_api_base=str(file_config.get("llm_api_base", os.getenv("LLM_API_BASE", "http://127.0.0.1:8000/v1"))).rstrip("/"),
            llm_api_key=str(file_config.get("llm_api_key", os.getenv("LLM_API_KEY", ""))),
            llm_model=str(file_config.get("llm_model", os.getenv("LLM_MODEL", "qwen"))),
            llm_timeout_seconds=int(os.getenv("LLM_TIMEOUT_SECONDS", "60")),
            llm_thinking_mode=str(file_config.get("llm_thinking_mode", os.getenv("LLM_THINKING_MODE", "disabled"))).strip().lower(),
            llm_max_tokens=int(file_config.get("llm_max_tokens", os.getenv("LLM_MAX_TOKENS", "700"))),
            llm_json_mode=str(file_config.get("llm_json_mode", os.getenv("LLM_JSON_MODE", "true"))).strip().lower() in {"1", "true", "yes", "on"},
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
