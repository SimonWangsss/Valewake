import argparse
import json
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from stardew_backend.action_policy import normalize_action_proposal, requested_action


def ratio(numerator: int, denominator: int) -> float:
    return round(numerator / denominator, 4) if denominator else 0.0


def f1(precision: float, recall: float) -> float:
    return round(2 * precision * recall / (precision + recall), 4) if precision + recall else 0.0


def load_cases(path: Path) -> list[dict[str, Any]]:
    return [
        json.loads(line)
        for line in path.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]


def evaluate(cases: list[dict[str, Any]]) -> dict[str, Any]:
    labels = sorted({str(case.get("expected_action", "")) for case in cases})
    confusion: dict[str, Counter[str]] = defaultdict(Counter)
    failures = []
    proposal_passed = 0
    proposal_total = 0
    priority_passed = 0
    priority_total = 0

    for case in cases:
        expected = str(case.get("expected_action", ""))
        predicted = requested_action(str(case.get("text", "")))
        confusion[expected][predicted] += 1
        if predicted != expected:
            failures.append({
                "id": case.get("id"),
                "text": case.get("text"),
                "expected": expected,
                "predicted": predicted,
            })
        if expected:
            proposal_total += 1
            proposal = normalize_action_proposal(
                {
                    "action": expected,
                    "disposition": "accept",
                    "confidence": 0.9,
                    "parameters": {"max_targets": 99},
                },
                str(case.get("text", "")),
            )
            valid = bool(
                proposal
                and proposal.get("action") == expected
                and proposal.get("requires_confirmation") is True
                and proposal.get("evidence")
                and proposal.get("evidence") in str(case.get("text", ""))
                and 1 <= int(proposal.get("parameters", {}).get("max_targets", 0)) <= 10
            )
            proposal_passed += int(valid)
            if "expected_priority" in case:
                priority_total += 1
                actual_priority = (proposal or {}).get("parameters", {}).get("resource_priority")
                priority_passed += int(actual_priority == case["expected_priority"])

    per_class = {}
    for label in labels:
        tp = confusion[label][label]
        fp = sum(confusion[other][label] for other in labels if other != label)
        fn = sum(count for predicted, count in confusion[label].items() if predicted != label)
        precision = ratio(tp, tp + fp)
        recall = ratio(tp, tp + fn)
        per_class[label or "none"] = {
            "support": sum(confusion[label].values()),
            "precision": precision,
            "recall": recall,
            "f1": f1(precision, recall),
        }

    expected_positive = sum(1 for case in cases if case.get("expected_action"))
    expected_negative = len(cases) - expected_positive
    predicted_positive = sum(sum(count for label, count in row.items() if label) for row in confusion.values())
    true_positive = sum(confusion[label][label] for label in labels if label)
    false_positive = sum(confusion[""][label] for label in labels if label)
    exact = sum(confusion[label][label] for label in labels)
    detection_precision = ratio(true_positive, predicted_positive)
    detection_recall = ratio(true_positive, expected_positive)
    return {
        "suite": "valewake-action-routing-v1",
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "cases": len(cases),
        "exact_intent_accuracy": ratio(exact, len(cases)),
        "macro_f1": round(sum(item["f1"] for item in per_class.values()) / len(per_class), 4),
        "action_detection": {
            "precision": detection_precision,
            "recall": detection_recall,
            "f1": f1(detection_precision, detection_recall),
            "false_positive_rate": ratio(false_positive, expected_negative),
        },
        "proposal_contract_rate": ratio(proposal_passed, proposal_total),
        "resource_priority_accuracy": ratio(priority_passed, priority_total),
        "per_class": per_class,
        "failures": failures,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Evaluate deterministic Valewake action routing.")
    parser.add_argument("--cases", type=Path, default=Path(__file__).with_name("action_golden.jsonl"))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = evaluate(load_cases(args.cases))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not report["failures"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
