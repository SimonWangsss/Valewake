import argparse
import json
from pathlib import Path
from typing import Any


FORBIDDEN_TERMS = ["as an ai", "language model", "system prompt", "api key", "backend"]
VALID_EMOTIONS = {"neutral", "happy", "sad", "angry", "affectionate"}


def grounded_relationship(record: dict[str, Any]) -> bool:
    effect = record.get("relationship_effect") or {}
    if effect.get("valence") == "neutral" or int(effect.get("intensity", 0) or 0) == 0:
        return True
    evidence = str(effect.get("evidence", "")).strip().lower()
    player_input = str(record.get("player_input", "")).lower()
    return bool(evidence) and evidence in player_input


def policy_enforced(record: dict[str, Any]) -> bool | None:
    policy = record.get("dialogue_policy")
    if not isinstance(policy, dict):
        return None
    effect = record.get("relationship_effect") or {}
    if policy.get("block_positive_relationship_effect") and effect.get("valence") == "positive":
        return False
    return True


def score(path: Path) -> dict[str, Any]:
    records = []
    errors = []
    if path.exists():
        for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            if not line.strip():
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError:
                errors.append({"line": line_number, "error": "invalid_json"})

    checks = {
        "nonempty_reply": [],
        "no_role_leakage": [],
        "grounded_relationship": [],
        "valid_emotion": [],
        "policy_enforced": [],
    }
    failures = []
    for record in records:
        turn_id = record.get("turn_id", "unknown")
        reply = str(record.get("reply", ""))
        values: dict[str, bool | None] = {
            "nonempty_reply": bool(reply.strip()),
            "no_role_leakage": not any(term in reply.lower() for term in FORBIDDEN_TERMS),
            "grounded_relationship": grounded_relationship(record),
            "valid_emotion": str(record.get("emotion", "")).lower() in VALID_EMOTIONS if "emotion" in record else None,
            "policy_enforced": policy_enforced(record),
        }
        for name, value in values.items():
            if value is None:
                continue
            checks[name].append(value)
            if not value:
                failures.append({"turn_id": turn_id, "check": name})

    metrics = {}
    for name, values in checks.items():
        metrics[name] = {
            "applicable": len(values),
            "passed": sum(values),
            "rate": round(sum(values) / len(values), 3) if values else None,
        }
    applicable = sum(len(values) for values in checks.values())
    passed = sum(sum(values) for values in checks.values())
    return {
        "trace_path": str(path),
        "turns": len(records),
        "invalid_json_lines": errors,
        "overall_check_rate": round(passed / applicable, 3) if applicable else None,
        "metrics": metrics,
        "failures": failures[:50],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Score Stardew Agent dialogue traces without an LLM judge.")
    parser.add_argument(
        "path",
        nargs="?",
        type=Path,
        default=Path("data/traces/agent_trace.jsonl"),
    )
    args = parser.parse_args()
    report = score(args.path)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if report["invalid_json_lines"] or report["failures"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
