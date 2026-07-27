import unittest
import json
from pathlib import Path

from stardew_backend.action_policy import (
    normalize_action_proposal,
    requested_action,
)


class ActionIntentTests(unittest.TestCase):
    def test_detects_chinese_watering_request(self) -> None:
        self.assertEqual(
            requested_action(
                "\u4f60\u80fd\u5e2e\u6211\u7ed9\u9644\u8fd1\u7684"
                "\u7530\u5730\u6d47\u6c34\u5417\uff1f"
            ),
            "water_crops",
        )

    def test_detects_chinese_weeding_request(self) -> None:
        self.assertEqual(
            requested_action(
                "\u53ef\u4ee5\u5e2e\u6211\u6e05\u7406\u519c\u573a"
                "\u91cc\u7684\u6742\u8349\u5417\uff1f"
            ),
            "clear_weeds",
        )

    def test_ordinary_dialogue_has_no_action(self) -> None:
        self.assertEqual(
            requested_action(
                "\u4f60\u4eca\u5929\u8fc7\u5f97\u600e\u4e48\u6837\uff1f"
            ),
            "",
        )


class ActionProposalTests(unittest.TestCase):
    def test_accepted_request_is_typed_and_grounded(self) -> None:
        player_input = (
            "\u4f60\u80fd\u5e2e\u6211\u7ed9\u9644\u8fd1\u7684"
            "\u7530\u5730\u6d47\u6c34\u5417\uff1f"
        )
        result = normalize_action_proposal(
            {
                "action": "water_crops",
                "disposition": "accept",
                "confidence": 0.4,
                "parameters": {"max_targets": 25},
                "evidence": (
                    "\u6a21\u578b\u6539\u5199\u540e\u4f46\u5e76\u975e"
                    "\u73a9\u5bb6\u539f\u8bdd"
                ),
            },
            player_input,
        )

        self.assertIsNotNone(result)
        assert result is not None
        self.assertEqual(result["action"], "water_crops")
        self.assertEqual(result["disposition"], "accept")
        self.assertEqual(result["parameters"]["max_targets"], 10)
        self.assertEqual(result["evidence"], player_input)
        self.assertGreaterEqual(result["confidence"], 0.8)
        self.assertTrue(result["requires_confirmation"])

    def test_npc_refusal_remains_a_refusal(self) -> None:
        result = normalize_action_proposal(
            {
                "action": "clear_weeds",
                "disposition": "refuse",
                "confidence": 0.95,
                "evidence": "\u6e05\u7406\u6742\u8349",
            },
            "\u5e2e\u6211\u6e05\u7406\u6742\u8349\u5427",
        )

        self.assertIsNotNone(result)
        assert result is not None
        self.assertEqual(result["disposition"], "refuse")

    def test_mismatched_action_is_discarded(self) -> None:
        result = normalize_action_proposal(
            {
                "action": "clear_weeds",
                "disposition": "accept",
                "confidence": 0.9,
            },
            "\u8bf7\u5e2e\u6211\u6d47\u6c34",
        )
        self.assertIsNone(result)

    def test_unsupported_action_is_discarded(self) -> None:
        result = normalize_action_proposal(
            {
                "action": "buy_items",
                "disposition": "accept",
                "confidence": 0.9,
            },
            "\u8bf7\u5e2e\u6211\u4e70\u79cd\u5b50",
        )
        self.assertIsNone(result)


class ActionLoreConsistencyTests(unittest.TestCase):
    def test_farm_action_lore_matches_executable_capabilities(self) -> None:
        lore_path = (
            Path(__file__).resolve().parents[1]
            / "data"
            / "rag"
            / "stardew"
            / "social_policy_extended.jsonl"
        )
        chunks = [
            json.loads(line)
            for line in lore_path.read_text(encoding="utf-8").splitlines()
            if line.strip()
        ]
        chunk = next(
            item
            for item in chunks
            if item["id"] == "action_farm_boundary_001"
        )
        self.assertIn("Watering and weeding are available", chunk["text"])
        self.assertIn("Season alone does not prove", chunk["text"])
        self.assertNotIn("cannot water", chunk["text"])

    def test_capability_lore_matches_bounded_executor(self) -> None:
        lore_path = (
            Path(__file__).resolve().parents[1]
            / "data"
            / "rag"
            / "stardew"
            / "agent_capabilities.jsonl"
        )
        text = lore_path.read_text(encoding="utf-8")
        self.assertIn("confirmed watering", text)
        self.assertIn("strict weed-clearing", text)
        self.assertNotIn("cannot yet move the NPC", text)


if __name__ == "__main__":
    unittest.main()
