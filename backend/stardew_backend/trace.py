import json
from datetime import datetime, timezone
from pathlib import Path
from threading import Lock
from typing import Any, Dict
from uuid import uuid4


class TraceStore:
    def __init__(self, path: Path):
        self.path = path
        self._lock = Lock()

    def append(self, record: Dict[str, Any]) -> str:
        turn_id = f"turn_{uuid4().hex[:12]}"
        payload = {
            "turn_id": turn_id,
            "created_at": datetime.now(timezone.utc).isoformat(),
            **record,
        }
        self.path.parent.mkdir(parents=True, exist_ok=True)
        line = json.dumps(payload, ensure_ascii=False)
        with self._lock:
            with self.path.open("a", encoding="utf-8") as handle:
                handle.write(line + "\n")
        return turn_id
