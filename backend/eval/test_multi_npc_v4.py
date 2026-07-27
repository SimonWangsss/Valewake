import tempfile
import unittest
from pathlib import Path

from stardew_backend.agent import StardewAgent
from stardew_backend.config import Settings
from stardew_backend.persona import PersonaRegistry
from stardew_backend.retrieval import RagStore


BACKEND_ROOT = Path(__file__).resolve().parents[1]


def mock_settings(root: Path) -> Settings:
    return Settings(
        llm_backend="mock",
        llm_api_base="http://127.0.0.1:8000/v1",
        llm_api_key="",
        llm_model="mock",
        llm_timeout_seconds=5,
        rag_dir=BACKEND_ROOT / "data" / "rag" / "stardew",
        memory_path=root / "memory.json",
        trace_path=root / "trace.jsonl",
        top_k_rag=5,
        top_k_memory=4,
        max_episodes_per_session=80,
        persona_path=BACKEND_ROOT / "data" / "personas" / "stardew_npcs.json",
    )


def game_state(npc_name: str) -> dict:
    return {
        "npc": {"name": npc_name, "display_name": npc_name},
        "npc_perception": {
            "time": {
                "year": 1,
                "season": "spring",
                "dayOfMonth": 8,
                "timeOfDay": 1200,
            },
            "weather": {"isRaining": False},
            "location": {"name": "Town"},
            "player": {
                "name": "Tester",
                "hearts": 2,
                "relationshipStatus": "friends",
            },
            "nearby": {"npcs": [], "objects": [], "monsters": []},
        },
    }


class PersonaRegistryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.registry = PersonaRegistry(
            BACKEND_ROOT / "data" / "personas" / "stardew_npcs.json"
        )

    def test_base_game_social_roster_is_curated(self) -> None:
        self.assertGreaterEqual(self.registry.curated_count, 33)
        self.assertEqual("curated", self.registry.get("Sebastian")["profile_source"])

    def test_modded_npc_uses_bounded_fallback(self) -> None:
        profile = self.registry.get("CustomCompanion")
        self.assertEqual("generic_fallback", profile["profile_source"])
        self.assertIn("resident", profile["identity"].lower())

    def test_modded_child_uses_runtime_age_boundary(self) -> None:
        profile = self.registry.get(
            "CustomChild",
            runtime_age_group="child",
        )
        self.assertEqual("child", profile["age_group"])

    def test_children_have_explicit_safety_boundaries(self) -> None:
        for npc_name in ("Jas", "Leo", "Vincent"):
            with self.subTest(npc=npc_name):
                profile = self.registry.get(npc_name)
                self.assertEqual("child", profile["age_group"])
                self.assertTrue(any("romantic" in item for item in profile["boundaries"]))


class MultiNpcIsolationTests(unittest.TestCase):
    def test_abigail_lore_does_not_leak_to_sebastian(self) -> None:
        rag = RagStore(BACKEND_ROOT / "data" / "rag" / "stardew")
        results = rag.search_details(
            "What games and music do you like?",
            8,
            tags=["sebastian"],
            npc_name="Sebastian",
        )
        self.assertFalse(any("abigail" in item["tags"] for item in results))

    def test_prompt_uses_selected_persona(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            agent = StardewAgent(mock_settings(Path(temporary)))
            result = agent.chat(
                "What do you do for work?",
                game_state("Sebastian"),
                "save-a:Sebastian",
                debug=True,
            )
            prompt = result["debug_prompt"]
            self.assertIn("programmer", prompt)
            self.assertIn("Sebastian", prompt)
            self.assertNotIn("Keep Abigail's tone", prompt)

    def test_memory_isolated_by_npc_session(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            agent = StardewAgent(mock_settings(Path(temporary)))
            agent.chat(
                "I like fishing.",
                game_state("Willy"),
                "save-a:Willy",
            )
            self.assertTrue(agent.memory.search("fishing", "save-a:Willy", 4))
            self.assertEqual(
                [],
                agent.memory.search("fishing", "save-a:Abigail", 4),
            )

    def test_child_prompt_contains_hard_boundary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            agent = StardewAgent(mock_settings(Path(temporary)))
            result = agent.chat(
                "Do you want to play?",
                game_state("Jas"),
                "save-a:Jas",
                debug=True,
            )
            self.assertIn("never produce romantic", result["debug_prompt"].lower())


if __name__ == "__main__":
    unittest.main()
