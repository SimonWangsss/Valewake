import argparse
import json
from pathlib import Path
from typing import Any


def flatten(prefix: str, value: Any, output: dict[str, float]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            flatten(f"{prefix}.{key}" if prefix else key, child, output)
    elif isinstance(value, (int, float)) and not isinstance(value, bool):
        output[prefix] = float(value)


def main() -> int:
    parser = argparse.ArgumentParser(description="Compare two Stardew golden evaluation reports.")
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    args = parser.parse_args()
    baseline = json.loads(args.baseline.read_text(encoding="utf-8"))
    candidate = json.loads(args.candidate.read_text(encoding="utf-8"))
    left: dict[str, float] = {}
    right: dict[str, float] = {}
    flatten("", baseline.get("summary", {}), left)
    flatten("", candidate.get("summary", {}), right)
    rows = []
    for metric in sorted(set(left) & set(right)):
        rows.append({
            "metric": metric,
            "baseline": left[metric],
            "candidate": right[metric],
            "delta": round(right[metric] - left[metric], 4),
        })
    output = {
        "same_test_set": baseline.get("test_set_sha256") == candidate.get("test_set_sha256"),
        "baseline": {
            "path": str(args.baseline),
            "version": baseline.get("mod_version"),
            "model": baseline.get("llm_model"),
        },
        "candidate": {
            "path": str(args.candidate),
            "version": candidate.get("mod_version"),
            "model": candidate.get("llm_model"),
        },
        "metrics": rows,
    }
    print(json.dumps(output, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
