import argparse
import hashlib
import json
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    rows = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            value = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"{path}:{line_number}: invalid JSON") from exc
        if isinstance(value, dict):
            rows.append(value)
    return rows


def pseudonym(value: str, salt: str) -> str:
    digest = hashlib.sha256(f"{salt}:{value}".encode("utf-8")).hexdigest()[:12]
    return f"anon_{digest}"


def latest_annotations(rows: Iterable[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        turn_id = str(row.get("turn_id", ""))
        if turn_id:
            grouped[turn_id].append(row)
    return grouped


def save_status_for_turn(
    events: list[dict[str, Any]],
    session_prefix: str,
    created_at: str,
) -> str:
    """Use the first save boundary after a turn, not the latest boundary in the file."""
    candidates = []
    for row in events:
        event = str(row.get("event", ""))
        payload = row.get("payload") if isinstance(row.get("payload"), dict) else row
        if str(payload.get("session_prefix", "")) != session_prefix:
            continue
        event_time = str(row.get("created_at", ""))
        if created_at and event_time and event_time < created_at:
            continue
        if event in {"save_committed", "save_rolled_back", "save_loaded_rollback"}:
            candidates.append((event_time, event))
    if not candidates:
        return "pending"
    event = sorted(candidates)[0][1]
    return "committed" if event == "save_committed" else "rolled_back"


def linked_events(
    events: list[dict[str, Any]],
    action_rows: list[dict[str, Any]],
) -> tuple[dict[str, list[dict[str, Any]]], dict[str, list[dict[str, Any]]]]:
    by_turn: dict[str, list[dict[str, Any]]] = defaultdict(list)
    by_proposal: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in [*events, *action_rows]:
        turn_id = str(row.get("turn_id", ""))
        proposal_id = str(row.get("proposal_id", ""))
        if turn_id:
            by_turn[turn_id].append(row)
        if proposal_id:
            by_proposal[proposal_id].append(row)
    return by_turn, by_proposal


def main() -> int:
    parser = argparse.ArgumentParser(description="Export curated Valewake dialogue records.")
    parser.add_argument("--trace", type=Path, default=Path("data/traces/agent_trace.jsonl"))
    parser.add_argument("--events", type=Path)
    parser.add_argument("--annotations", type=Path)
    parser.add_argument("--action-trace", type=Path)
    parser.add_argument("--expedition-trace", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--only-marked",
        choices=[
            "keep", "reject", "boundary", "memory_good", "memory_bad",
            "action_good", "action_bad",
        ],
    )
    parser.add_argument("--include-rolled-back", action="store_true")
    parser.add_argument("--no-anonymize", action="store_true")
    parser.add_argument("--salt", default="valewake-local-export")
    args = parser.parse_args()

    events_path = args.events or args.trace.with_name("dataset_events.jsonl")
    annotations_path = args.annotations or args.trace.with_name("dataset_annotations.jsonl")
    trace_rows = read_jsonl(args.trace)
    events = read_jsonl(events_path)
    annotations = latest_annotations(read_jsonl(annotations_path))
    action_rows = read_jsonl(args.action_trace) if args.action_trace else []
    expedition_rows = read_jsonl(args.expedition_trace) if args.expedition_trace else []
    action_rows.extend(expedition_rows)
    by_turn, by_proposal = linked_events(events, action_rows)

    exported = []
    seen = set()
    for row in trace_rows:
        turn_id = str(row.get("turn_id", ""))
        if not turn_id or not row.get("player_input"):
            continue
        labels = annotations.get(turn_id, [])
        if args.only_marked and not any(item.get("label") == args.only_marked for item in labels):
            continue
        session_id = str(row.get("session_id", ""))
        session_prefix = session_id.split(":", 1)[0] + ":" if ":" in session_id else session_id
        save_status = save_status_for_turn(events, session_prefix, str(row.get("created_at", "")))
        if save_status == "rolled_back" and not args.include_rolled_back:
            continue
        duplicate_key = (
            str(row.get("npc", "")).lower(),
            str(row.get("player_input", "")).strip().lower(),
            str(row.get("reply", "")).strip().lower(),
        )
        if duplicate_key in seen:
            continue
        seen.add(duplicate_key)

        proposal = row.get("action_proposal") if isinstance(row.get("action_proposal"), dict) else {}
        proposal_id = str(proposal.get("proposal_id", ""))
        perception = row.get("perception") if isinstance(row.get("perception"), dict) else {}
        player = perception.get("player") if isinstance(perception.get("player"), dict) else {}
        record = {
            "record_schema": "valewake-dataset-1",
            "turn_id": turn_id,
            "created_at": row.get("created_at"),
            "save_status": save_status,
            "session_id": (
                session_id
                if args.no_anonymize
                else pseudonym(session_id, args.salt)
            ),
            "npc": row.get("npc"),
            "player_name": (
                player.get("name")
                if args.no_anonymize
                else pseudonym(str(player.get("name", "")), args.salt)
            ),
            "messages": [
                *row.get("conversation_history", []),
                {"role": "user", "content": row.get("player_input", "")},
                {"role": "assistant", "content": row.get("reply", "")},
            ],
            "policy": row.get("dialogue_policy", {}),
            "memory": {
                "retrieved": row.get("retrieved_memory", []),
                "written": row.get("memory_written", []),
            },
            "relationship_effect": row.get("relationship_effect", {}),
            "action_proposal": row.get("action_proposal"),
            "annotations": labels,
            "outcome_events": by_turn.get(turn_id, []),
            "action_events": by_proposal.get(proposal_id, []) if proposal_id else [],
        }
        exported.append(record)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8") as handle:
        for row in exported:
            handle.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(json.dumps({
        "ok": True,
        "exported": len(exported),
        "source_turns": len(trace_rows),
        "output": str(args.output),
        "only_marked": args.only_marked,
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
