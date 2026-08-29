import argparse
import json
import shutil
import sys
import tempfile
from collections import Counter
from pathlib import Path
from typing import Any


BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings


def load_scenarios(path: Path) -> list[dict[str, Any]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise ValueError("Memory scenario file must contain a JSON array.")
    seen: set[str] = set()
    scenarios: list[dict[str, Any]] = []
    for index, item in enumerate(data, 1):
        if not isinstance(item, dict):
            raise ValueError(f"Scenario #{index} is not an object.")
        scenario_id = str(item.get("scenario_id", "")).strip()
        if not scenario_id or scenario_id in seen:
            raise ValueError(f"Missing or duplicate scenario_id at #{index}: {scenario_id!r}")
        if not isinstance(item.get("steps"), list):
            raise ValueError(f"Scenario {scenario_id} has no steps array.")
        seen.add(scenario_id)
        scenarios.append(item)
    return scenarios


def game_state(day: int, npc: str) -> dict[str, Any]:
    day = max(1, day)
    zero_based = day - 1
    season_index = (zero_based % 112) // 28
    return {
        "source": "memory_simulator",
        "npc": {"name": npc, "display_name": npc, "age_group": "adult"},
        "npc_perception": {
            "schemaVersion": "npc-perception-memory-sim-1",
            "time": {
                "year": zero_based // 112 + 1,
                "season": ["spring", "summer", "fall", "winter"][season_index],
                "dayOfMonth": zero_based % 28 + 1,
                "timeOfDay": 1200,
            },
            "weather": {"isRaining": False},
            "location": {"name": "Town"},
            "player": {
                "name": "MemoryEvalFarmer",
                "hearts": 6,
                "friendshipPoints": 1500,
                "relationshipStatus": "friends",
                "heldItem": "",
            },
            "nearby": {"npcs": [], "objects": [], "crops": [], "monsters": []},
        },
        "agent_relationship": {"rapport": 0, "trust": 0},
        "action_rules": {"enabled": False},
    }


def contains_all(values: list[str], expected: list[str]) -> tuple[bool, list[str]]:
    joined = "\n".join(values).lower()
    missing = [value for value in expected if value.lower() not in joined]
    return not missing, missing


def contains_none(values: list[str], forbidden: list[str]) -> tuple[bool, list[str]]:
    joined = "\n".join(values).lower()
    present = [value for value in forbidden if value.lower() in joined]
    return not present, present


def active_memory_texts(agent: StardewAgent, session_id: str) -> list[str]:
    return [
        str(item.get("text", ""))
        for item in agent.memory.data["memories"]
        if item.get("session_id") == session_id and item.get("status", "active") == "active"
    ]


def superseded_memory_texts(agent: StardewAgent, session_id: str) -> list[str]:
    return [
        str(item.get("text", ""))
        for item in agent.memory.data["memories"]
        if item.get("session_id") == session_id and item.get("status") == "superseded"
    ]


def settings_for(root: Path, max_episodes: int) -> Settings:
    return Settings(
        llm_backend="mock",
        llm_api_base="",
        llm_api_key="",
        llm_model="memory-simulator-mock",
        llm_timeout_seconds=5,
        rag_dir=BACKEND_ROOT / "data" / "rag" / "stardew",
        memory_path=root / "memory.json",
        trace_path=root / "trace.jsonl",
        top_k_rag=3,
        top_k_memory=4,
        max_episodes_per_session=max_episodes,
        persona_path=BACKEND_ROOT / "data" / "personas" / "stardew_npcs.json",
    )


def run_scenario(scenario: dict[str, Any], root: Path) -> dict[str, Any]:
    scenario_id = str(scenario["scenario_id"])
    scenario_root = root / scenario_id
    scenario_root.mkdir(parents=True, exist_ok=True)
    agent = StardewAgent(settings_for(
        scenario_root, int(scenario.get("max_episodes_per_session", 80))
    ))
    default_npc = str(scenario.get("npc", "Abigail"))
    default_session = str(scenario.get("session_id", f"memory-sim:{scenario_id}:{default_npc}"))
    step_results: list[dict[str, Any]] = []
    assertion_count = 0
    failures: list[str] = []

    def check(
        condition: bool,
        step_index: int,
        label: str,
        detail: str,
        checks: list[dict[str, Any]],
    ) -> None:
        nonlocal assertion_count
        assertion_count += 1
        checks.append({"label": label, "passed": condition, "detail": detail})
        if not condition:
            failures.append(f"step {step_index} {label}: {detail}")

    for step_index, step in enumerate(scenario["steps"], 1):
        operation = str(step.get("op", "chat"))
        day = int(step.get("day", 1))
        npc = str(step.get("npc", default_npc))
        session_id = str(step.get("session_id", default_session))
        checks: list[dict[str, Any]] = []
        observed: dict[str, Any] = {}

        if operation == "chat":
            result = agent.chat(
                player_input=str(step.get("input", "")),
                game_state=game_state(day, npc),
                session_id=session_id,
            )
            observed = {
                "saved": result["saved_memories"],
                "retrieved": result["retrieved_memory"],
                "reply": result["reply"],
            }
        elif operation == "search":
            observed["retrieved"] = agent.memory.search(
                str(step.get("query", "")), session_id,
                int(step.get("top_k", 4)), game_day=day,
            )
        elif operation == "commit":
            agent.memory.commit_session(str(step.get("session_prefix", session_id)))
            observed["operation"] = "committed"
        elif operation == "rollback":
            agent.memory.rollback_session(str(step.get("session_prefix", session_id)))
            observed["operation"] = "rolled_back"
        elif operation == "seed_memory":
            added = agent.memory.add(
                str(step.get("text", "")),
                session_id=session_id,
                kind=str(step.get("kind", "preference")),
                importance=int(step.get("importance", 2)),
                evidence=str(step.get("evidence", step.get("text", ""))),
                confidence=float(step.get("confidence", 1.0)),
                game_day=day,
                source="memory_simulator",
            )
            observed["added"] = added
        elif operation == "repeat_episode":
            count = int(step.get("count", 1))
            for offset in range(count):
                agent.memory.record_episode(
                    session_id=session_id,
                    player_input=f"{step.get('input', 'episode')} #{offset}",
                    reply=str(step.get("reply", "recorded")),
                    emotion="neutral",
                    game_day=day + offset,
                    policy={},
                )
            observed["episode_count"] = len(agent.memory.recent_episodes(session_id, 10_000))
        elif operation == "assert_memory":
            observed = {
                "active": active_memory_texts(agent, session_id),
                "superseded": superseded_memory_texts(agent, session_id),
                "profile": agent.memory.durable_profile(session_id, limit=20),
                "episode_count": len(agent.memory.recent_episodes(session_id, 10_000)),
            }
        else:
            raise ValueError(f"Scenario {scenario_id} step {step_index}: unsupported op {operation!r}")

        expectations = step.get("expect") if isinstance(step.get("expect"), dict) else {}
        text_expectations = {
            "saved_contains": observed.get("saved", []),
            "retrieved_contains": observed.get("retrieved", []),
            "active_contains": active_memory_texts(agent, session_id),
            "superseded_contains": superseded_memory_texts(agent, session_id),
            "profile_contains": agent.memory.durable_profile(session_id, 20),
        }
        for expectation_key, values in text_expectations.items():
            expected = [str(value) for value in expectations.get(expectation_key, [])]
            if expected:
                passed, missing = contains_all([str(value) for value in values], expected)
                check(passed, step_index, expectation_key, f"missing={missing}; observed={values}", checks)

        exclusion_expectations = {
            "saved_excludes": observed.get("saved", []),
            "retrieved_excludes": observed.get("retrieved", []),
            "active_excludes": active_memory_texts(agent, session_id),
            "superseded_excludes": superseded_memory_texts(agent, session_id),
        }
        for expectation_key, values in exclusion_expectations.items():
            forbidden = [str(value) for value in expectations.get(expectation_key, [])]
            if forbidden:
                passed, present = contains_none([str(value) for value in values], forbidden)
                check(passed, step_index, expectation_key, f"present={present}; observed={values}", checks)

        if "episode_count" in expectations:
            actual_count = len(agent.memory.recent_episodes(session_id, 10_000))
            expected_count = int(expectations["episode_count"])
            check(
                actual_count == expected_count,
                step_index,
                "episode_count",
                f"expected={expected_count}; actual={actual_count}",
                checks,
            )
        if "active_count" in expectations:
            actual_count = len(active_memory_texts(agent, session_id))
            expected_count = int(expectations["active_count"])
            check(
                actual_count == expected_count,
                step_index,
                "active_count",
                f"expected={expected_count}; actual={actual_count}",
                checks,
            )

        step_results.append({
            "step": step_index,
            "day": day,
            "op": operation,
            "session_id": session_id,
            "passed": all(item["passed"] for item in checks),
            "checks": checks,
            "observed": observed,
        })

    return {
        "scenario_id": scenario_id,
        "category": scenario.get("category", "uncategorized"),
        "day_span": max((int(step.get("day", 1)) for step in scenario["steps"]), default=1),
        "passed": not failures,
        "step_count": len(step_results),
        "assertion_count": assertion_count,
        "failures": failures,
        "steps": step_results,
        "artifacts": {
            "memory": str(scenario_root / "memory.json"),
            "trace": str(scenario_root / "trace.jsonl"),
        },
    }


def run_scenarios(scenarios: list[dict[str, Any]], root: Path) -> dict[str, Any]:
    results = [run_scenario(scenario, root) for scenario in scenarios]
    by_category: dict[str, dict[str, int | float]] = {}
    counts = Counter(str(result["category"]) for result in results)
    passes = Counter(str(result["category"]) for result in results if result["passed"])
    for category, total in sorted(counts.items()):
        by_category[category] = {
            "total": total,
            "passed": passes[category],
            "pass_rate": round(passes[category] / total, 4),
        }
    total_assertions = sum(int(result["assertion_count"]) for result in results)
    passed_assertions = sum(
        sum(check["passed"] for step in result["steps"] for check in step["checks"])
        for result in results
    )
    return {
        "suite": "valewake-memory-timeline-simulator-v1",
        "deterministic": True,
        "llm_backend": "mock",
        "scenario_count": len(results),
        "passed_scenarios": sum(result["passed"] for result in results),
        "scenario_pass_rate": round(
            sum(result["passed"] for result in results) / len(results), 4
        ) if results else None,
        "assertion_count": total_assertions,
        "passed_assertions": passed_assertions,
        "assertion_pass_rate": round(passed_assertions / total_assertions, 4)
        if total_assertions else None,
        "max_game_day": max((int(result["day_span"]) for result in results), default=0),
        "by_category": by_category,
        "results": results,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Run deterministic 7-30 game-day Valewake memory scenarios without Stardew/SMAPI."
    )
    parser.add_argument(
        "--scenarios",
        type=Path,
        default=Path(__file__).with_name("memory_scenarios.json"),
    )
    parser.add_argument("--output", type=Path)
    parser.add_argument("--artifacts", type=Path)
    args = parser.parse_args()
    scenarios = load_scenarios(args.scenarios)
    temporary: tempfile.TemporaryDirectory[str] | None = None
    if args.artifacts:
        root = args.artifacts
        if root.exists():
            shutil.rmtree(root)
        root.mkdir(parents=True)
    else:
        temporary = tempfile.TemporaryDirectory(prefix="valewake-memory-sim-")
        root = Path(temporary.name)
    report = run_scenarios(scenarios, root)
    report["artifacts_retained"] = args.artifacts is not None
    if temporary is not None:
        for result in report["results"]:
            result["artifacts"] = None
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if temporary is not None:
        temporary.cleanup()
    return 0 if report["passed_scenarios"] == report["scenario_count"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
