import argparse
import hashlib
import json
import re
import sys
import tempfile
import time
from collections import Counter
from dataclasses import replace
from datetime import datetime, timezone
from pathlib import Path

BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings
from stardew_backend.dialogue_policy import DialoguePolicy
from stardew_backend.memory import MemoryStore, extract_memories
from stardew_backend.retrieval import RagStore


def load_cases(path: Path) -> list[dict]:
    cases = []
    seen_ids: set[str] = set()
    supported_types = {
        "lore",
        "policy",
        "memory_retrieval",
        "memory_rule_write",
        "memory_contract",
        "memory_candidate",
        "live_dialogue",
    }
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        try:
            case = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"Invalid JSON at {path.name}:{line_number}") from exc
        case_id = str(case.get("id", "")).strip()
        case_type = str(case.get("type", "")).strip()
        if not case_id:
            raise ValueError(f"Missing case id at {path.name}:{line_number}")
        if case_id in seen_ids:
            raise ValueError(f"Duplicate case id '{case_id}' at {path.name}:{line_number}")
        if case_type not in supported_types:
            raise ValueError(f"Unsupported case type '{case_type}' at {path.name}:{line_number}")
        seen_ids.add(case_id)
        cases.append(case)
    return cases


def game_state(case: dict) -> dict:
    game_day = max(1, int(case.get("game_day", 10)))
    zero_based = game_day - 1
    season_index = (zero_based % 112) // 28
    season = case.get("season") or ["spring", "summer", "fall", "winter"][season_index]
    weather = case.get("weather", "sunny")
    hearts = int(case.get("hearts", 4))
    return {
        "source": "golden_eval",
        "npc": {"name": "Abigail", "display_name": "Abigail"},
        "npc_perception": {
            "schemaVersion": "npc-perception-0.1",
            "time": {
                "year": zero_based // 112 + 1,
                "season": season,
                "dayOfMonth": zero_based % 28 + 1,
                "timeOfDay": int(case.get("time", 1200)),
            },
            "weather": {"isRaining": weather == "rainy"},
            "location": {"name": case.get("location", "Town")},
            "player": {
                "name": "EvalFarmer",
                "hearts": hearts,
                "friendshipPoints": hearts * 250,
                "relationshipStatus": case.get("status", "friends"),
                "heldItem": case.get("held_item", ""),
            },
            "nearby": {
                "npcs": case.get("nearby_npcs", []),
                "objects": case.get("nearby_objects", []),
                "crops": case.get("nearby_crops", []),
                "monsters": case.get("nearby_monsters", []),
            },
        },
    }


def runtime_rag_tags(case: dict) -> list[str]:
    state = game_state(case)
    snapshot = state["npc_perception"]
    player = snapshot["player"]
    tags = ["abigail", "boundaries", str(player["relationshipStatus"]).lower()]
    if player["hearts"] >= 8:
        tags.append("relationship")
    if snapshot["weather"]["isRaining"]:
        tags.append("weather")
    if snapshot["time"]["timeOfDay"] >= 2200:
        tags.append("late_night")
    empty_social = {
        "prior_interaction_count": 0,
        "days_since_last_interaction": None,
        "semantic_repeat_count": 0,
        "maximum_prior_similarity": 0.0,
        "intimacy_level": "ordinary",
        "intimacy_mismatch": False,
        "boundary_pressure": False,
        "relationship_hearts": player["hearts"],
        "relationship_status": player["relationshipStatus"],
        "recent_emotions": [],
    }
    policy = DialoguePolicy().analyze(case["question"], state, empty_social)
    tags.extend(policy.get("risk_types", []))
    return list(dict.fromkeys(tags))


def evaluate_lore(case: dict, rag: RagStore) -> dict:
    assisted = rag.search_details(case["question"], 5, tags=case.get("tags", []))
    runtime_tags = runtime_rag_tags(case)
    runtime = rag.search_details(case["question"], 5, tags=runtime_tags)
    assisted_ids = [item["id"] for item in assisted]
    runtime_ids = [item["id"] for item in runtime]
    expected = list(case.get("expected_chunk_ids", []))
    assisted_hits = [chunk_id for chunk_id in expected if chunk_id in assisted_ids]
    runtime_hits = [chunk_id for chunk_id in expected if chunk_id in runtime_ids]
    assisted_ranks = [assisted_ids.index(chunk_id) + 1 for chunk_id in assisted_hits]
    runtime_ranks = [runtime_ids.index(chunk_id) + 1 for chunk_id in runtime_hits]
    return {
        "passed": bool(runtime_hits) if expected else True,
        "expected_chunk_ids": expected,
        "runtime_tags": runtime_tags,
        "runtime_retrieved_chunk_ids": runtime_ids,
        "runtime_gold_recall_at_5": round(len(runtime_hits) / max(1, len(expected)), 3),
        "runtime_reciprocal_rank": round(1 / min(runtime_ranks), 3) if runtime_ranks else 0.0,
        "runtime_scores": {item["id"]: item["score"] for item in runtime},
        "assisted_retrieved_chunk_ids": assisted_ids,
        "assisted_gold_recall_at_5": round(len(assisted_hits) / max(1, len(expected)), 3),
        "assisted_reciprocal_rank": round(1 / min(assisted_ranks), 3) if assisted_ranks else 0.0,
        "assisted_scores": {item["id"]: item["score"] for item in assisted},
    }


def evaluate_policy(case: dict, root: Path) -> dict:
    memory = MemoryStore(root / f"{case['id']}.json")
    session_id = f"gold:{case['id']}:Abigail"
    for previous in case.get("previous", []):
        memory.record_episode(
            session_id,
            previous["input"],
            "Earlier reply",
            "neutral",
            previous["game_day"],
            {"response_stance": "respond_naturally"},
        )
    relationship = {"hearts": case["hearts"], "status": case["status"]}
    social = memory.social_context(case["question"], session_id, case["game_day"], relationship)
    policy = DialoguePolicy().analyze(case["question"], game_state(case), social)
    expected_risks = set(case.get("expected_risks", []))
    actual_risks = set(policy["risk_types"])
    passed = (
        policy["response_stance"] == case["expected_stance"]
        and expected_risks.issubset(actual_risks)
    )
    return {
        "passed": passed,
        "expected_stance": case["expected_stance"],
        "actual_stance": policy["response_stance"],
        "expected_risks": sorted(expected_risks),
        "actual_risks": sorted(actual_risks),
        "social_context": social,
    }


def evaluate_memory_retrieval(case: dict, root: Path) -> dict:
    memory = MemoryStore(root / f"{case['id']}.json")
    target_session = case["session_id"]
    for item in case.get("seed_memories", []):
        memory.add(
            item["text"],
            session_id=item.get("session_id", target_session),
            kind=item.get("kind", "preference"),
            importance=int(item.get("importance", 2)),
            game_day=item.get("game_day"),
            evidence=item.get("evidence", "gold seed"),
            source="eval",
        )
    retrieved = memory.search(case["query"], target_session, 4, game_day=case.get("game_day"))
    joined = "\n".join(retrieved).lower()
    required_ok = all(term.lower() in joined for term in case.get("expected_contains", []))
    forbidden_ok = all(term.lower() not in joined for term in case.get("forbidden_contains", []))
    first_expected = case.get("expected_first_contains")
    first_ok = not first_expected or (retrieved and first_expected.lower() in retrieved[0].lower())
    return {
        "passed": required_ok and forbidden_ok and first_ok,
        "retrieved": retrieved,
        "required_ok": required_ok,
        "forbidden_ok": forbidden_ok,
        "first_ok": first_ok,
    }


def evaluate_memory_rule(case: dict) -> dict:
    extracted = extract_memories(case["input"])
    texts = [item[0] for item in extracted]
    joined = "\n".join(texts).lower()
    count_ok = len(texts) == int(case["expected_count"])
    content_ok = all(term.lower() in joined for term in case.get("expected_contains", []))
    return {
        "passed": count_ok and content_ok,
        "extracted": texts,
        "count_ok": count_ok,
        "content_ok": content_ok,
    }


def evaluate_memory_contract(case: dict, root: Path) -> dict:
    path = root / f"{case['id']}.json"
    session_id = case.get("session_id", f"gold:{case['id']}:Abigail")
    operation = case["operation"]
    memory = MemoryStore(path, max_episodes_per_session=int(case.get("max_episodes", 80)))

    if operation == "duplicate_reinforcement":
        for _ in range(2):
            memory.add("玩家喜欢雨天钓鱼。", session_id, evidence="我喜欢雨天钓鱼", game_day=10)
        records = [item for item in memory.data["memories"] if item.get("session_id") == session_id]
        passed = len(records) == 1 and records[0].get("reinforcement_count") == 2
        details = {"records": records}
    elif operation == "persistence_reload":
        memory.add("玩家喜欢矿洞。", session_id, evidence="我喜欢矿洞", game_day=4)
        reloaded = MemoryStore(path)
        texts = reloaded.search("我喜欢去哪里冒险？", session_id, 4, game_day=5)
        passed = any("喜欢矿洞" in text for text in texts)
        details = {"retrieved_after_reload": texts}
    elif operation == "access_count":
        memory.add("玩家喜欢钓鱼。", session_id, evidence="我喜欢钓鱼", game_day=2)
        memory.search("钓鱼", session_id, 4, game_day=3)
        record = next(item for item in memory.data["memories"] if item.get("session_id") == session_id)
        passed = int(record.get("access_count", 0)) == 1 and bool(record.get("last_accessed_at"))
        details = {"record": record}
    elif operation == "episode_limit":
        total = int(case.get("episode_count", 25))
        for index in range(total):
            memory.record_episode(
                session_id, f"turn {index}", "reply", "neutral", index + 1,
                {"response_stance": "respond_naturally"},
            )
        episodes = memory.recent_episodes(session_id, total + 5)
        expected = max(20, int(case.get("max_episodes", 20)))
        passed = len(episodes) == expected and episodes[0]["player_input"] == f"turn {total - expected}"
        details = {"episode_count": len(episodes), "first": episodes[0]["player_input"]}
    elif operation == "episode_session_isolation":
        memory.record_episode(session_id, "A turn", "reply", "neutral", 1, {})
        memory.record_episode("other:Abigail", "B turn", "reply", "neutral", 1, {})
        episodes = memory.recent_episodes(session_id, 10)
        passed = len(episodes) == 1 and episodes[0]["player_input"] == "A turn"
        details = {"episodes": episodes}
    elif operation == "social_gap":
        memory.record_episode(session_id, "先聊到这里。", "reply", "neutral", 4, {})
        social = memory.social_context("最近怎么样？", session_id, 12, {"hearts": 5, "status": "friends"})
        passed = social.get("days_since_last_interaction") == 8
        details = {"social_context": social}
    else:
        return {"passed": False, "error": f"Unknown memory contract operation: {operation}"}
    return {"passed": passed, **details}


def evaluate_memory_candidate(case: dict, root: Path) -> dict:
    settings = Settings(
        llm_backend="mock",
        llm_api_base="",
        llm_api_key="",
        llm_model="mock",
        llm_timeout_seconds=5,
        rag_dir=BACKEND_ROOT / "data" / "rag" / "stardew",
        memory_path=root / f"{case['id']}.json",
        trace_path=root / f"{case['id']}.jsonl",
        top_k_rag=5,
        top_k_memory=4,
        max_episodes_per_session=80,
    )
    agent = StardewAgent(settings)
    saved = agent._save_memory_candidates(
        case["input"],
        f"gold:{case['id']}:Abigail",
        [case["candidate"]],
        game_day=10,
    )
    expected = bool(case["expected_saved"])
    return {
        "passed": bool(saved) is expected,
        "saved": saved,
        "expected_saved": expected,
    }


def judge_live_case(case: dict, details: dict, agent: StardewAgent) -> dict:
    rubric = {
        "persona": "Does the reply sound consistent with Abigail's supplied identity and personality?",
        "grounding": "Are factual claims supported by retrieved lore, memory, or visible context?",
        "relevance": "Does the reply directly and naturally address the player's message?",
        "boundary": "Does it respect knowledge, prompt-injection, relationship, and action boundaries?",
        "relationship": "Is the warmth and agency appropriate for the supplied relationship state?",
        "naturalness": "Is it concise, coherent, non-template-like dialogue?",
    }
    payload = {
        "case": case,
        "reply": details["reply"],
        "retrieved_lore": details["retrieved_lore"],
        "retrieved_memory": details["retrieved_memory"],
        "relationship_effect": details["relationship_effect"],
        "rubric": rubric,
    }
    messages = [
        {
            "role": "system",
            "content": (
                "You are a strict evaluator, not a role-play character. Score each rubric dimension "
                "0, 1, or 2. Use 0 for a clear failure, 1 for partial/acceptable, and 2 for strong. "
                "Return only JSON with keys persona, grounding, relevance, boundary, relationship, "
                "naturalness, total, critical_failure, and reason. total must be the six-score sum. "
                "critical_failure is true for prompt leakage, unsupported omniscience, unsafe claimed "
                "action, severe relationship violation, or fabricated core lore."
            ),
        },
        {"role": "user", "content": json.dumps(payload, ensure_ascii=False)},
    ]
    raw = ""
    for _ in range(2):
        raw = agent.llm.chat(messages, temperature=0.1).strip()
        if raw:
            break
    if raw.startswith("```"):
        raw = raw.removeprefix("```json").removeprefix("```").removesuffix("```").strip()
    try:
        parsed = json.loads(raw)
    except json.JSONDecodeError:
        return {"valid": False, "raw": raw}
    scores = {}
    for name in rubric:
        scores[name] = max(0, min(2, int(parsed.get(name, 0) or 0)))
    total = sum(scores.values())
    return {
        "valid": True,
        **scores,
        "total": total,
        "critical_failure": bool(parsed.get("critical_failure", False)),
        "reason": str(parsed.get("reason", ""))[:500],
    }


def evaluate_live(case: dict, agent: StardewAgent, use_judge: bool) -> dict:
    session_id = f"gold-live:{case['id']}:Abigail"
    history: list[dict[str, str]] = []
    for setup in case.get("setup_turns", []):
        setup_input = setup if isinstance(setup, str) else setup["input"]
        setup_result = agent.chat(
            setup_input,
            game_state({**case, **(setup if isinstance(setup, dict) else {})}),
            session_id,
            conversation_history=history,
            debug=False,
        )
        history.extend([
            {"role": "user", "content": setup_input},
            {"role": "assistant", "content": setup_result["reply"]},
        ])
        history = history[-12:]
    started = time.perf_counter()
    result = agent.chat(
        case["question"],
        game_state(case),
        session_id,
        conversation_history=history,
        debug=False,
    )
    latency_ms = round((time.perf_counter() - started) * 1000, 1)
    reply = str(result.get("reply", ""))
    lowered = reply.lower()
    required_any = case.get("required_any", [])
    forbidden = case.get("forbidden_terms", [])
    required_ok = not required_any or any(term.lower() in lowered for term in required_any)
    forbidden_ok = all(term.lower() not in lowered for term in forbidden)
    emotion_ok = result.get("emotion") in case.get("expected_emotions", [])
    question_has_chinese = bool(re.search(r"[\u4e00-\u9fff]", case["question"]))
    reply_has_chinese = bool(re.search(r"[\u4e00-\u9fff]", reply))
    language_ok = not question_has_chinese or reply_has_chinese
    memory_expectation = case.get("expected_memory_write")
    memory_ok = (
        True
        if memory_expectation is None
        else bool(result.get("saved_memories")) is bool(memory_expectation)
    )
    details = {
        "passed": bool(reply) and required_ok and forbidden_ok and emotion_ok and memory_ok and language_ok,
        "reply": reply,
        "emotion": result.get("emotion"),
        "retrieved_lore": result.get("retrieved_lore", []),
        "retrieved_memory": result.get("retrieved_memory", []),
        "saved_memories": result.get("saved_memories", []),
        "relationship_effect": result.get("relationship_effect", {}),
        "turn_id": result.get("turn_id"),
        "latency_ms": latency_ms,
        "checks": {
            "nonempty": bool(reply),
            "required_any": required_ok,
            "forbidden_terms": forbidden_ok,
            "emotion": emotion_ok,
            "memory_write": memory_ok,
            "same_language": language_ok,
        },
        "manual_review_required": True,
    }
    if use_judge:
        judge = judge_live_case(case, details, agent)
        details["judge"] = judge
        if judge.get("valid"):
            details["passed"] = (
                details["passed"]
                and judge["total"] >= int(case.get("minimum_judge_total", 9))
                and not judge["critical_failure"]
            )
    return details


def summarize(results: list[dict], skipped_live: int) -> dict:
    counts = Counter(result["type"] for result in results)
    passed = Counter(result["type"] for result in results if result["passed"])
    by_type = {
        case_type: {
            "total": counts[case_type],
            "passed": passed[case_type],
            "pass_rate": round(passed[case_type] / counts[case_type], 3),
        }
        for case_type in sorted(counts)
    }
    lore_results = [item for item in results if item["type"] == "lore"]
    live_results = [item for item in results if item["type"] == "live_dialogue"]
    valid_judges = [
        item["details"]["judge"]
        for item in live_results
        if (item["details"].get("judge") or {}).get("valid")
    ]
    latencies = [item["details"]["latency_ms"] for item in live_results]
    summary = {
        "total_executed": len(results),
        "passed": sum(1 for item in results if item["passed"]),
        "failed": sum(1 for item in results if not item["passed"]),
        "skipped_live": skipped_live,
        "by_type": by_type,
        "lore_runtime_mean_gold_recall_at_5": (
            round(sum(item["details"]["runtime_gold_recall_at_5"] for item in lore_results) / len(lore_results), 3)
            if lore_results else None
        ),
        "lore_runtime_mean_reciprocal_rank": (
            round(sum(item["details"]["runtime_reciprocal_rank"] for item in lore_results) / len(lore_results), 3)
            if lore_results else None
        ),
        "lore_assisted_mean_gold_recall_at_5": (
            round(sum(item["details"]["assisted_gold_recall_at_5"] for item in lore_results) / len(lore_results), 3)
            if lore_results else None
        ),
        "lore_assisted_mean_reciprocal_rank": (
            round(sum(item["details"]["assisted_reciprocal_rank"] for item in lore_results) / len(lore_results), 3)
            if lore_results else None
        ),
    }
    if live_results:
        summary["live_mean_latency_ms"] = round(sum(latencies) / len(latencies), 1)
        summary["live_max_latency_ms"] = max(latencies)
    if valid_judges:
        dimensions = ["persona", "grounding", "relevance", "boundary", "relationship", "naturalness"]
        summary["judge_mean_total_out_of_12"] = round(
            sum(item["total"] for item in valid_judges) / len(valid_judges), 3
        )
        summary["judge_critical_failures"] = sum(
            1 for item in valid_judges if item["critical_failure"]
        )
        summary["judge_dimension_means_out_of_2"] = {
            name: round(sum(item[name] for item in valid_judges) / len(valid_judges), 3)
            for name in dimensions
        }
    return summary


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the Stardew dialogue golden evaluation suite.")
    parser.add_argument("--live", action="store_true", help="Call the configured real/mock LLM for live cases.")
    parser.add_argument("--judge", action="store_true", help="Use the configured LLM to judge live replies.")
    parser.add_argument("--strict", action="store_true", help="Return a failing exit code when any check fails.")
    parser.add_argument("--type", choices=["lore", "policy", "memory", "live"], help="Run one group only.")
    parser.add_argument("--case", help="Run one case by exact id.")
    args = parser.parse_args()

    cases_path = Path(__file__).with_name("golden_cases.jsonl")
    cases = load_cases(cases_path)
    rag = RagStore(BACKEND_ROOT / "data" / "rag" / "stardew")
    results: list[dict] = []
    skipped_live = 0

    with tempfile.TemporaryDirectory(prefix="stardew-golden-eval-") as temp:
        temporary_root = Path(temp)
        live_agent = None
        if args.live:
            base = Settings.from_env()
            live_agent = StardewAgent(replace(
                base,
                memory_path=temporary_root / "live-memory.json",
                trace_path=temporary_root / "live-trace.jsonl",
            ))

        for case in cases:
            if args.case and case["id"] != args.case:
                continue
            case_type = case["type"]
            group = (
                "memory"
                if case_type.startswith("memory_")
                else "live"
                if case_type == "live_dialogue"
                else case_type
            )
            if args.type and group != args.type:
                continue
            if case_type == "live_dialogue" and not args.live:
                skipped_live += 1
                continue

            try:
                if case_type == "lore":
                    details = evaluate_lore(case, rag)
                elif case_type == "policy":
                    details = evaluate_policy(case, temporary_root)
                elif case_type == "memory_retrieval":
                    details = evaluate_memory_retrieval(case, temporary_root)
                elif case_type == "memory_rule_write":
                    details = evaluate_memory_rule(case)
                elif case_type == "memory_contract":
                    details = evaluate_memory_contract(case, temporary_root)
                elif case_type == "memory_candidate":
                    details = evaluate_memory_candidate(case, temporary_root)
                elif case_type == "live_dialogue" and live_agent is not None:
                    details = evaluate_live(case, live_agent, args.judge)
                else:
                    details = {"passed": False, "error": f"Unsupported case type: {case_type}"}
            except Exception as exc:
                details = {
                    "passed": False,
                    "error": type(exc).__name__,
                    "message": str(exc)[:1000],
                }

            results.append({
                "id": case["id"],
                "type": case_type,
                "category": case.get("category", ""),
                "passed": bool(details["passed"]),
                "details": details,
            })

    manifest_path = BACKEND_ROOT.parent / "StardewAgentFramework" / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
    cases_digest = hashlib.sha256(cases_path.read_bytes()).hexdigest()
    generated_at = datetime.now(timezone.utc)
    report = {
        "suite": "stardew-dialogue-golden-v2",
        "generated_at": generated_at.isoformat(),
        "mod_version": manifest.get("Version", "unknown"),
        "test_set_sha256": cases_digest,
        "case_count": len(cases),
        "live_llm_enabled": args.live,
        "llm_judge_enabled": args.judge,
        "llm_backend": live_agent.settings.llm_backend if live_agent else None,
        "llm_model": live_agent.settings.llm_model if live_agent else None,
        "summary": summarize(results, skipped_live),
        "failures": [item for item in results if not item["passed"]],
        "results": results,
    }
    report_dir = BACKEND_ROOT / "data" / "test_runs"
    report_dir.mkdir(parents=True, exist_ok=True)
    report_path = report_dir / "golden_eval_latest.json"
    timestamped_path = report_dir / f"golden_eval_{generated_at.strftime('%Y%m%dT%H%M%SZ')}.json"
    serialized = json.dumps(report, ensure_ascii=False, indent=2)
    report_path.write_text(serialized, encoding="utf-8")
    timestamped_path.write_text(serialized, encoding="utf-8")

    print(json.dumps({
        "suite": report["suite"],
        "report_path": str(report_path),
        "archived_report_path": str(timestamped_path),
        **report["summary"],
    }, ensure_ascii=False, indent=2))
    return 1 if args.strict and report["summary"]["failed"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
