import json
from datetime import datetime, timezone
from pathlib import Path
from threading import Lock
from typing import Any, Dict
from uuid import uuid4


class TraceStore:
    def __init__(self, path: Path):
        self.path = path
        self.event_path = path.with_name("dataset_events.jsonl")
        self.annotation_path = path.with_name("dataset_annotations.jsonl")
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

    def append_event(self, event: str, payload: Dict[str, Any]) -> str:
        return self._append_auxiliary(
            self.event_path,
            {"event_id": f"event_{uuid4().hex[:12]}", "event": event, **payload},
        )

    def append_annotation(
        self,
        turn_id: str,
        label: str,
        note: str = "",
        tags: list[str] | None = None,
    ) -> str:
        return self._append_auxiliary(
            self.annotation_path,
            {
                "annotation_id": f"annotation_{uuid4().hex[:12]}",
                "turn_id": turn_id,
                "label": label,
                "tags": tags or [],
                "note": note[:500],
            },
        )

    def _append_auxiliary(self, path: Path, payload: Dict[str, Any]) -> str:
        record = {
            "created_at": datetime.now(timezone.utc).isoformat(),
            **payload,
        }
        path.parent.mkdir(parents=True, exist_ok=True)
        with self._lock:
            with path.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(record, ensure_ascii=False) + "\n")
        return str(
            payload.get("event_id")
            or payload.get("annotation_id")
            or ""
        )
