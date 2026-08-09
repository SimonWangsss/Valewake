import unittest

from tools.export_dataset import linked_events, save_status_for_turn


class DatasetExportTests(unittest.TestCase):
    def test_uses_first_save_boundary_after_turn(self) -> None:
        events = [
            {"created_at": "2026-01-01T10:00:00Z", "event": "save_committed", "session_prefix": "Farm_1:"},
            {"created_at": "2026-01-01T11:00:00Z", "event": "save_rolled_back", "session_prefix": "Farm_1:"},
            {"created_at": "2026-01-01T12:00:00Z", "event": "save_committed", "session_prefix": "Farm_1:"},
        ]
        self.assertEqual(
            save_status_for_turn(events, "Farm_1:", "2026-01-01T10:30:00Z"),
            "rolled_back",
        )
        self.assertEqual(
            save_status_for_turn(events, "Farm_1:", "2026-01-01T11:30:00Z"),
            "committed",
        )

    def test_links_dialogue_proposal_and_expedition(self) -> None:
        by_turn, by_proposal = linked_events(
            [{"event": "relationship_applied", "turn_id": "turn_1"}],
            [{
                "event_name": "node_mined",
                "turn_id": "turn_1",
                "proposal_id": "proposal_1",
                "expedition_id": "exp_1",
            }],
        )
        self.assertEqual(len(by_turn["turn_1"]), 2)
        self.assertEqual(by_proposal["proposal_1"][0]["expedition_id"], "exp_1")


if __name__ == "__main__":
    unittest.main()
