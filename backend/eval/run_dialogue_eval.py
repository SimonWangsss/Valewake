import json
import sys
import tempfile
from pathlib import Path

BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from stardew_backend.dialogue_policy import DialoguePolicy
from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings
from stardew_backend.memory import MemoryStore
from stardew_backend.retrieval import RagStore


def game_state(game_day: int, hearts: int, status: str) -> dict:
    zero_based = max(0, game_day - 1)
    year = zero_based // 112 + 1
    season_day = zero_based % 112
    season_index = season_day // 28
    day_of_month = season_day % 28 + 1
    season = ["spring", "summer", "fall", "winter"][season_index]
    return {
        "npc": {"name": "Abigail", "display_name": "Abigail"},
        "npc_perception": {
            "time": {"year": year, "season": season, "dayOfMonth": day_of_month},
            "player": {"hearts": hearts, "relationshipStatus": status},
        },
    }


def run_case(case: dict, rag: RagStore, temporary_root: Path) -> tuple[bool, dict]:
    case_type = case["type"]
    if case_type == "policy":
        memory = MemoryStore(temporary_root / f"{case['id']}.json")
        session_id = f"eval:{case['id']}:Abigail"
        for previous in case.get("previous", []):
            memory.record_episode(
                session_id, previous["input"], "Earlier reply", "neutral",
                previous["game_day"], {"response_stance": "respond_naturally"},
            )
        relationship = {"hearts": case["hearts"], "status": case["status"]}
        social = memory.social_context(case["input"], session_id, case["game_day"], relationship)
        policy = DialoguePolicy().analyze(
            case["input"], game_state(case["game_day"], case["hearts"], case["status"]), social
        )
        risks_ok = all(risk in policy["risk_types"] for risk in case.get("expected_risks", []))
        passed = policy["response_stance"] == case["expected_stance"] and risks_ok
        return passed, {"stance": policy["response_stance"], "risks": policy["risk_types"], "social": social}

    if case_type == "rag":
        results = rag.search(case["input"], 5, tags=case.get("tags", []))
        passed = any(case["expected_contains"].lower() in result.lower() for result in results)
        return passed, {"retrieved": results}

    if case_type == "memory":
        memory = MemoryStore(temporary_root / f"{case['id']}.json")
        memory.add(case["memory"], "eval:memory", importance=2, game_day=1)
        results = memory.search(case["query"], "eval:memory", 4, game_day=2)
        passed = any(case["expected_contains"].lower() in result.lower() for result in results)
        return passed, {"retrieved": results}

    return False, {"error": f"Unknown case type: {case_type}"}


def main() -> int:
    cases_path = Path(__file__).with_name("cases.jsonl")
    cases = [json.loads(line) for line in cases_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    rag = RagStore(BACKEND_ROOT / "data" / "rag" / "stardew")
    results = []
    with tempfile.TemporaryDirectory(prefix="stardew-dialogue-eval-") as temporary_directory:
        root = Path(temporary_directory)
        for case in cases:
            passed, details = run_case(case, rag, root)
            results.append({"id": case["id"], "type": case["type"], "passed": passed, "details": details})

        settings = Settings(
            llm_backend="mock",
            llm_api_base="",
            llm_api_key="",
            llm_model="mock",
            llm_timeout_seconds=5,
            rag_dir=BACKEND_ROOT / "data" / "rag" / "stardew",
            memory_path=root / "agent-memory.json",
            trace_path=root / "agent-trace.jsonl",
            top_k_rag=5,
            top_k_memory=4,
            max_episodes_per_session=80,
        )
        agent = StardewAgent(settings)
        state = game_state(10, 4, "friends")
        responses = [agent.chat("Do you like coffee?", state, "eval:agent") for _ in range(3)]
        traces = [json.loads(line) for line in settings.trace_path.read_text(encoding="utf-8").splitlines()]
        contract_passed = (
            all(response.get("emotion") in {"neutral", "happy", "sad", "angry", "affectionate"} for response in responses)
            and traces[-1]["dialogue_policy"]["response_stance"] == "challenge_repetition"
            and len(agent.memory.recent_episodes("eval:agent")) == 3
        )
        results.append({
            "id": "agent_end_to_end_contract",
            "type": "contract",
            "passed": contract_passed,
            "details": {
                "final_stance": traces[-1]["dialogue_policy"]["response_stance"],
                "episode_count": len(agent.memory.recent_episodes("eval:agent")),
            },
        })

        legacy_path = root / "legacy-memory.json"
        legacy_path.write_text(json.dumps({
            "memories": [{
                "id": "legacy", "session_id": "eval:legacy", "kind": "preference",
                "text": "Player likes mining.", "importance": 2,
                "created_at": "2026-01-01T00:00:00+00:00",
            }]
        }), encoding="utf-8")
        legacy = MemoryStore(legacy_path)
        legacy_results = legacy.search("mining", "eval:legacy", 4, game_day=10)
        results.append({
            "id": "legacy_memory_migration",
            "type": "contract",
            "passed": legacy.data["schema_version"] == 2 and "Player likes mining." in legacy_results,
            "details": {"retrieved": legacy_results},
        })

    passed_count = sum(1 for result in results if result["passed"])
    report = {
        "suite": "dialogue-system-v2-offline",
        "total": len(results),
        "passed": passed_count,
        "failed": len(results) - passed_count,
        "pass_rate": round(passed_count / max(1, len(results)), 3),
        "failures": [result for result in results if not result["passed"]],
    }
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["failed"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
