import argparse
import json
from collections import Counter
from pathlib import Path
from typing import Any


TERMINAL_EVENTS = {"completed", "failed", "cancelled", "recovered_after_reload"}
TARGET_ACTIONS = {"water_crops", "clear_weeds", "chop_trees", "mine_target", "mine_nearby", "mine_expedition"}


def read_jsonl(path: Path) -> tuple[list[dict[str, Any]], list[int]]:
    if not path.exists():
        return [], []
    rows = []
    invalid = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            invalid.append(number)
            continue
        if isinstance(row, dict):
            rows.append(row)
    return rows, invalid


def ratio(numerator: int, denominator: int) -> float | None:
    return round(numerator / denominator, 4) if denominator else None


def score_group(rows: list[dict[str, Any]], id_field: str) -> dict[str, Any]:
    accepted = {str(row[id_field]) for row in rows if row.get("event_name") == "accepted" and row.get(id_field)}
    latest_terminal: dict[str, dict[str, Any]] = {}
    for row in rows:
        item_id = str(row.get(id_field, ""))
        if item_id and row.get("event_name") in TERMINAL_EVENTS:
            latest_terminal[item_id] = row
    completed = sum(row.get("event_name") == "completed" for row in latest_terminal.values())
    productive_completed = sum(
        row.get("event_name") == "completed" and (
            row.get("action") not in TARGET_ACTIONS or int(row.get("completed_targets", 0) or 0) > 0
        )
        for row in latest_terminal.values()
    )
    failed = sum(row.get("event_name") in {"failed", "recovered_after_reload"} for row in latest_terminal.values())
    cancelled = sum(row.get("event_name") == "cancelled" for row in latest_terminal.values())
    terminal = len(latest_terminal)
    target_completed = sum(int(row.get("completed_targets", 0) or 0) for row in latest_terminal.values())
    target_failed = sum(int(row.get("failed_targets", 0) or 0) for row in latest_terminal.values())
    linked = sum(
        bool(row.get("turn_id") and row.get("proposal_id"))
        for row in rows
        if row.get("event_name") == "accepted" and row.get(id_field)
    )
    restoration_rows = [
        row for row in latest_terminal.values()
        if "schedule_restored" in row and "location_restored" in row
    ]
    restoration_passed = sum(
        row.get("schedule_restored") is True and row.get("location_restored") is True
        for row in restoration_rows
    )
    verified_targets = [row for row in rows if row.get("event_name") in {"target_completed", "background_target_completed", "node_mined"} and "target_allowed" in row]
    allowlist_passed = sum(row.get("target_allowed") is True for row in verified_targets)
    return {
        "accepted": len(accepted),
        "terminal": terminal,
        "completed": completed,
        "productive_completed": productive_completed,
        "no_op_completed": completed - productive_completed,
        "failed": failed,
        "cancelled": cancelled,
        "task_success_rate": ratio(completed, terminal),
        "productive_task_success_rate": ratio(productive_completed, terminal),
        "terminal_coverage": ratio(terminal, len(accepted)),
        "target_success_rate": ratio(target_completed, target_completed + target_failed),
        "mean_completed_targets": ratio(target_completed, terminal),
        "trace_linkage_rate": ratio(linked, len(accepted)),
        "schedule_restore_rate": ratio(restoration_passed, len(restoration_rows)),
        "allowlist_invariant_rate": ratio(allowlist_passed, len(verified_targets)),
        "path_retry_events": sum(row.get("event_name") in {"target_path_retry", "return_path_retry"} for row in rows),
        "cross_location_events": sum(row.get("event_name") == "followed_across_location" for row in rows),
        "monsters_defeated": sum(row.get("event_name") == "monster_defeated" for row in rows),
        "nodes_mined": sum(row.get("event_name") == "node_mined" for row in rows),
        "sample_warning": "insufficient_real_game_sample" if terminal < 20 else None,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Score real Valewake farm and mine action traces.")
    parser.add_argument("--farm", type=Path, required=True)
    parser.add_argument("--expedition", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    farm_rows, farm_invalid = read_jsonl(args.farm)
    expedition_rows, expedition_invalid = read_jsonl(args.expedition)
    report = {
        "suite": "valewake-real-action-trace-v1",
        "farm_trace": str(args.farm),
        "expedition_trace": str(args.expedition),
        "farm_rows": len(farm_rows),
        "expedition_rows": len(expedition_rows),
        "invalid_json_lines": {"farm": farm_invalid, "expedition": expedition_invalid},
        "farm": score_group(farm_rows, "job_id"),
        "expedition": score_group(expedition_rows, "expedition_id"),
        "event_counts": {
            "farm": Counter(str(row.get("event_name", "")) for row in farm_rows),
            "expedition": Counter(str(row.get("event_name", "")) for row in expedition_rows),
        },
    }
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
