import argparse
import json
import math
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Any


TERMINAL_EVENTS = {"completed", "failed", "cancelled", "recovered_after_reload"}
TARGET_ACTIONS = {
    "water_crops", "clear_weeds", "chop_trees",
    "mine_target", "mine_nearby", "mine_expedition",
}


def read_jsonl(path: Path) -> tuple[list[dict[str, Any]], list[int]]:
    if not path.exists():
        return [], []
    rows: list[dict[str, Any]] = []
    invalid: list[int] = []
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


def ratio(numerator: int | float, denominator: int | float) -> float | None:
    return round(numerator / denominator, 4) if denominator else None


def percentile(values: list[float], quantile: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    position = (len(ordered) - 1) * quantile
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return round(ordered[lower], 3)
    value = ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower)
    return round(value, 3)


def parse_timestamp(value: Any) -> datetime | None:
    if not value:
        return None
    try:
        return datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    except ValueError:
        return None


def latest_by_event(
    rows: list[dict[str, Any]], id_field: str, events: set[str]
) -> dict[str, dict[str, Any]]:
    latest: dict[str, dict[str, Any]] = {}
    for row in rows:
        item_id = str(row.get(id_field, ""))
        if item_id and str(row.get("event_name", "")) in events:
            latest[item_id] = row
    return latest


def duration_metrics(
    rows: list[dict[str, Any]],
    id_field: str,
    terminals: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    accepted_rows = {
        str(row.get(id_field)): row
        for row in rows
        if row.get("event_name") == "accepted" and row.get(id_field)
    }
    active_seconds: list[float] = []
    wall_seconds: list[float] = []
    for item_id, terminal in terminals.items():
        ticks = terminal.get("active_duration_ticks")
        if isinstance(ticks, (int, float)) and ticks >= 0 and terminal.get("trace_schema"):
            active_seconds.append(float(ticks) / 60.0)
        accepted = accepted_rows.get(item_id)
        start = parse_timestamp((accepted or {}).get("timestamp"))
        end = parse_timestamp(terminal.get("timestamp"))
        if start is not None and end is not None:
            wall_seconds.append(max(0.0, (end - start).total_seconds()))
    return {
        "active_duration_sample": len(active_seconds),
        "active_duration_p50_seconds": percentile(active_seconds, 0.50),
        "active_duration_p95_seconds": percentile(active_seconds, 0.95),
        "wall_duration_sample": len(wall_seconds),
        "wall_duration_p50_seconds": percentile(wall_seconds, 0.50),
        "wall_duration_p95_seconds": percentile(wall_seconds, 0.95),
        "duration_note": "active duration uses game ticks; wall duration includes pauses, menus, and waiting",
    }


def score_group(
    rows: list[dict[str, Any]],
    id_field: str,
    *,
    include_by_action: bool = True,
) -> dict[str, Any]:
    accepted = {
        str(row[id_field])
        for row in rows
        if row.get("event_name") == "accepted" and row.get(id_field)
    }
    terminals = latest_by_event(rows, id_field, TERMINAL_EVENTS)
    postchecks = latest_by_event(rows, id_field, {"job_postcheck", "expedition_postcheck"})

    completed = sum(row.get("event_name") == "completed" for row in terminals.values())
    productive_completed = sum(
        row.get("event_name") == "completed"
        and (
            row.get("action") not in TARGET_ACTIONS
            or int(row.get("completed_targets", 0) or 0) > 0
        )
        for row in terminals.values()
    )
    failed = sum(
        row.get("event_name") in {"failed", "recovered_after_reload"}
        for row in terminals.values()
    )
    cancelled = sum(row.get("event_name") == "cancelled" for row in terminals.values())
    terminal_count = len(terminals)
    target_completed = sum(int(row.get("completed_targets", 0) or 0) for row in terminals.values())
    target_failed = sum(int(row.get("failed_targets", 0) or 0) for row in terminals.values())
    linked = sum(
        bool(row.get("turn_id") and row.get("proposal_id"))
        for row in rows
        if row.get("event_name") == "accepted" and row.get(id_field)
    )

    restoration_rows: list[dict[str, Any]] = []
    for item_id, terminal in terminals.items():
        candidate = postchecks.get(item_id, terminal)
        if "schedule_restored" in candidate and "location_restored" in candidate:
            restoration_rows.append(candidate)
    schedule_restored = sum(row.get("schedule_restored") is True for row in restoration_rows)
    location_restored = sum(row.get("location_restored") is True for row in restoration_rows)
    combined_restored = sum(
        row.get("schedule_restored") is True and row.get("location_restored") is True
        for row in restoration_rows
    )
    exact_tile_rows = [row for row in restoration_rows if "exact_tile_restored" in row]
    facing_rows = [row for row in restoration_rows if "facing_restored" in row]

    verified_targets = [
        row for row in rows
        if row.get("event_name") in {"target_completed", "background_target_completed", "node_mined"}
        and "target_allowed" in row
    ]
    allowlist_passed = sum(row.get("target_allowed") is True for row in verified_targets)
    mutation_rows = [
        row for row in rows
        if row.get("event_name") in {"target_completed", "node_mined"}
        and "mutation_observed" in row
    ]

    strict_world_diff_rows = [
        row for row in terminals.values()
        if row.get("trace_schema") == "valewake-action-trace-2"
        and isinstance(row.get("world_state_after"), dict)
        and isinstance(row.get("changed_world_keys"), list)
        and isinstance(row.get("unexpected_mutation_keys"), list)
    ]
    changed_world_keys = sum(len(row.get("changed_world_keys", [])) for row in strict_world_diff_rows)
    unintended_mutations = sum(len(row.get("unexpected_mutation_keys", [])) for row in strict_world_diff_rows)
    intended_mutations = max(0, changed_world_keys - unintended_mutations)

    opportunity_rows = [
        row for row in terminals.values()
        if row.get("trace_schema") == "valewake-action-trace-2"
        and "baseline_eligible_target_count" in row
        and row.get("action") in TARGET_ACTIONS
    ]
    opportunity_denominator = sum(
        min(
            int(row.get("baseline_eligible_target_count", 0) or 0),
            int(row.get("max_targets", 0) or 0),
        )
        for row in opportunity_rows
    )
    opportunity_completed = sum(
        min(
            int(row.get("completed_targets", 0) or 0),
            min(
                int(row.get("baseline_eligible_target_count", 0) or 0),
                int(row.get("max_targets", 0) or 0),
            ),
        )
        for row in opportunity_rows
    )

    v2_terminals = [row for row in terminals.values() if row.get("trace_schema") == "valewake-action-trace-2"]
    target_path_attempts = sum(int(row.get("target_path_attempt_count", 0) or 0) for row in v2_terminals)
    target_path_retries = sum(int(row.get("target_path_retry_count", 0) or 0) for row in v2_terminals)
    return_path_attempts = sum(int(row.get("return_path_attempt_count", 0) or 0) for row in v2_terminals)
    return_path_retries = sum(int(row.get("return_path_retry_count", 0) or 0) for row in v2_terminals)
    jobs_with_retry = sum(
        int(row.get("target_path_retry_count", 0) or 0)
        + int(row.get("return_path_retry_count", 0) or 0) > 0
        for row in v2_terminals
    )

    result: dict[str, Any] = {
        "accepted": len(accepted),
        "terminal": terminal_count,
        "completed": completed,
        "productive_completed": productive_completed,
        "no_op_completed": completed - productive_completed,
        "failed": failed,
        "cancelled": cancelled,
        "state_completion_rate": ratio(completed, terminal_count),
        "task_success_rate": ratio(completed, terminal_count),
        "productive_task_success_rate": ratio(productive_completed, terminal_count),
        "no_op_rate": ratio(completed - productive_completed, completed),
        "terminal_coverage": ratio(terminal_count, len(accepted)),
        "target_execution_success_rate": ratio(target_completed, target_completed + target_failed),
        "target_success_rate": ratio(target_completed, target_completed + target_failed),
        "mean_completed_targets": ratio(target_completed, terminal_count),
        "opportunity_recall": ratio(opportunity_completed, opportunity_denominator),
        "opportunity_recall_sample": len(opportunity_rows),
        "trace_linkage_rate": ratio(linked, len(accepted)),
        "schedule_restore_rate": ratio(schedule_restored, len(restoration_rows)),
        "location_restore_rate": ratio(location_restored, len(restoration_rows)),
        "combined_restore_rate": ratio(combined_restored, len(restoration_rows)),
        "exact_tile_restore_rate": ratio(
            sum(row.get("exact_tile_restored") is True for row in exact_tile_rows), len(exact_tile_rows)
        ),
        "facing_restore_rate": ratio(
            sum(row.get("facing_restored") is True for row in facing_rows), len(facing_rows)
        ),
        "delayed_postcheck_sample": len(postchecks),
        "allowlist_invariant_rate": ratio(allowlist_passed, len(verified_targets)),
        "target_mutation_verification_rate": ratio(
            sum(row.get("mutation_observed") is True for row in mutation_rows), len(mutation_rows)
        ),
        "target_precision": ratio(intended_mutations, changed_world_keys),
        "world_diff_sample": len(strict_world_diff_rows),
        "observed_world_mutations": changed_world_keys,
        "unintended_mutations": unintended_mutations,
        "jobs_with_path_retry_rate": ratio(jobs_with_retry, len(v2_terminals)),
        "target_path_attempts": target_path_attempts,
        "target_path_retries": target_path_retries,
        "target_path_retry_rate": ratio(target_path_retries, target_path_attempts),
        "return_path_attempts": return_path_attempts,
        "return_path_retries": return_path_retries,
        "return_path_retry_rate": ratio(return_path_retries, return_path_attempts),
        "legacy_path_retry_events": sum(
            row.get("event_name") in {"target_path_retry", "return_path_retry"} for row in rows
        ),
        "path_retry_events": sum(
            row.get("event_name") in {"target_path_retry", "return_path_retry"} for row in rows
        ),
        "cross_location_events": sum(row.get("event_name") == "followed_across_location" for row in rows),
        "monsters_defeated": sum(row.get("event_name") == "monster_defeated" for row in rows),
        "nodes_mined": sum(row.get("event_name") == "node_mined" for row in rows),
        "v2_terminal_sample": len(v2_terminals),
        "sample_warning": "insufficient_real_game_sample" if terminal_count < 20 else None,
        **duration_metrics(rows, id_field, terminals),
    }
    if include_by_action:
        actions = sorted({str(row.get("action")) for row in rows if row.get("action")})
        result["by_action"] = {
            action: score_group(
                [row for row in rows if str(row.get("action")) == action],
                id_field,
                include_by_action=False,
            )
            for action in actions
        }
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="Score real Valewake farm and mine action traces (v1 and v2).")
    parser.add_argument("--farm", type=Path, required=True)
    parser.add_argument("--expedition", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    farm_rows, farm_invalid = read_jsonl(args.farm)
    expedition_rows, expedition_invalid = read_jsonl(args.expedition)
    report = {
        "suite": "valewake-real-action-trace-v2",
        "metric_definitions": {
            "productive_task_success_rate": "productive completed / terminal accepted trials",
            "no_op_rate": "completed trials with zero completed targets / completed trials",
            "target_execution_success_rate": "completed selected targets / attempted selected targets",
            "opportunity_recall": "completed baseline-eligible targets / min(baseline eligible, max targets)",
            "target_precision": "intended observed world mutations / all observed world mutations",
            "target_path_retry_rate": "target path retries / target path attempts",
            "combined_restore_rate": "postchecks passing location and schedule flag restoration",
        },
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
