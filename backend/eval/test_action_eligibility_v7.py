import tempfile
import unittest
from pathlib import Path

from stardew_backend.action_policy import action_eligibility
from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings
from stardew_backend.dialogue_policy import player_knowledge_context, relationship_voice
from stardew_backend.memory import MemoryStore


def state(hearts: int, *, trust: int = 0, attempt_limit: int = 3) -> dict:
    return {
        "npc_perception": {"player": {"hearts": hearts}},
        "agent_relationship": {"trust": trust},
        "action_rules": {
            "enabled": True,
            "mine_expeditions_enabled": True,
            "minimum_farm_hearts": 2,
            "minimum_expedition_hearts": 4,
            "minimum_trust": 0,
            "current_trust": trust,
            "request_attempt_limit": attempt_limit,
            "current_time": 1200,
            "farm_end_time": 2200,
            "expedition_end_time": 2300,
            "is_host": True,
            "event_active": False,
            "npc_is_child": False,
        },
    }


class ActionEligibilityTests(unittest.TestCase):
    def test_low_hearts_rejects_mine_request_before_generation(self) -> None:
        result = action_eligibility("mine_expedition", state(3))
        self.assertFalse(result["eligible"])
        self.assertEqual("insufficient_hearts", result["reason_code"])
        self.assertEqual(4, result["minimum_hearts"])

    def test_farm_and_mine_use_different_thresholds(self) -> None:
        self.assertTrue(action_eligibility("water_crops", state(2))["eligible"])
        self.assertFalse(action_eligibility("join_mine_expedition", state(2))["eligible"])

    def test_hard_rejections_cannot_be_bypassed_by_repetition(self) -> None:
        value = state(10)
        value["action_rules"]["npc_is_child"] = True
        result = action_eligibility("water_crops", value)
        self.assertFalse(result["eligible"])
        self.assertEqual("child_npc", result["reason_code"])


class RefusalCounterTests(unittest.TestCase):
    def test_two_eligible_refusals_trigger_third_attempt_context(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            memory = MemoryStore(Path(folder) / "memory.json")
            context = {"requested_action": "mine_expedition", "eligible": True}
            for _ in range(2):
                memory.record_episode(
                    "save:Haley", "陪我下矿", "不去。", "neutral", 10, {},
                    action_context=context, action_disposition="refuse",
                )
            self.assertEqual(
                2,
                memory.consecutive_action_refusals("save:Haley", "mine_expedition", 10),
            )

    def test_ineligible_refusals_do_not_advance_guarantee(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            memory = MemoryStore(Path(folder) / "memory.json")
            memory.record_episode(
                "save:Haley", "陪我下矿", "不去。", "neutral", 10, {},
                action_context={"requested_action": "mine_expedition", "eligible": False},
                action_disposition="refuse",
            )
            self.assertEqual(
                0,
                memory.consecutive_action_refusals("save:Haley", "mine_expedition", 10),
            )

    def test_chat_forces_acceptance_on_third_eligible_refusal(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            settings = Settings(
                llm_backend="mock", llm_api_base="", llm_api_key="", llm_model="mock",
                llm_timeout_seconds=5, rag_dir=Path("data/rag/stardew"),
                memory_path=root / "memory.json", trace_path=root / "trace.jsonl",
                top_k_rag=2, top_k_memory=2, max_episodes_per_session=20,
                persona_path=Path("data/personas/stardew_npcs.json"),
            )
            agent = StardewAgent(settings)
            game_state = state(4)
            game_state["npc"] = {"name": "Haley", "display_name": "Haley", "age_group": "adult"}
            game_state["npc_perception"]["time"] = {
                "year": 1, "season": "spring", "dayOfMonth": 10,
            }
            replies = [
                agent.chat("陪我下矿探险，优先找铁矿", game_state, "save:Haley")
                for _ in range(3)
            ]
            self.assertEqual("refuse", replies[0]["action_proposal"]["disposition"])
            self.assertEqual("refuse", replies[1]["action_proposal"]["disposition"])
            self.assertEqual("accept", replies[2]["action_proposal"]["disposition"])

    def test_chat_never_accepts_below_required_hearts(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            agent = StardewAgent(Settings(
                llm_backend="mock", llm_api_base="", llm_api_key="", llm_model="mock",
                llm_timeout_seconds=5, rag_dir=Path("data/rag/stardew"),
                memory_path=root / "memory.json", trace_path=root / "trace.jsonl",
                top_k_rag=2, top_k_memory=2, max_episodes_per_session=20,
                persona_path=Path("data/personas/stardew_npcs.json"),
            ))
            game_state = state(3)
            game_state["npc"] = {"name": "Haley", "display_name": "Haley", "age_group": "adult"}
            for _ in range(3):
                result = agent.chat("陪我下矿探险，优先找铁矿", game_state, "save:Haley")
                self.assertEqual("refuse", result["action_proposal"]["disposition"])
                self.assertIn("还没熟", result["reply"])


class RelationshipVoiceTests(unittest.TestCase):
    def test_low_hearts_are_reserved(self) -> None:
        self.assertEqual("acquaintance", relationship_voice({"hearts": 1})["stage"])

    def test_high_hearts_without_dating_remain_platonic(self) -> None:
        voice = relationship_voice({"hearts": 10, "status": "friendly"})
        self.assertEqual("very_close", voice["stage"])
        self.assertIn("platonic", voice["guidance"])

    def test_marriage_uses_partner_voice(self) -> None:
        self.assertEqual(
            "committed_partner",
            relationship_voice({"hearts": 10, "status": "married"})["stage"],
        )


class PlayerKnowledgeTests(unittest.TestCase):
    def test_unknown_animal_question_allows_only_tentative_guess(self) -> None:
        context = player_knowledge_context(
            "你猜我喜欢什么动物？", [], {"hearts": 10, "status": "married"}
        )
        self.assertFalse(context["grounded_memory_available"])
        self.assertEqual("one_tentative_guess_then_ask", context["response_mode"])

    def test_unknown_specific_movie_prefers_clarification(self) -> None:
        context = player_knowledge_context(
            "你觉得我最喜欢什么电影？", [], {"hearts": 8, "status": "friendly"}
        )
        self.assertEqual("admit_unknown_then_ask", context["response_mode"])

    def test_unrelated_preference_memory_does_not_ground_movie_answer(self) -> None:
        context = player_knowledge_context(
            "你知道我喜欢什么电影吗？",
            ["Player enjoys mining."],
            {"hearts": 8, "status": "friendly"},
        )
        self.assertFalse(context["grounded_memory_available"])
        self.assertEqual("admit_unknown_then_ask", context["response_mode"])

    def test_matching_memory_can_ground_movie_answer(self) -> None:
        context = player_knowledge_context(
            "你知道我喜欢什么电影吗？",
            ["Player said their favorite movie is The Brave Little Sapling."],
            {"hearts": 8, "status": "friendly"},
        )
        self.assertTrue(context["grounded_memory_available"])
        self.assertEqual("answer_from_retrieved_memory", context["response_mode"])


if __name__ == "__main__":
    unittest.main()
