import unittest

from score_action_traces import score_group


class ActionTraceV2ScorerTests(unittest.TestCase):
    def test_scores_v2_world_diff_duration_retry_and_recall(self) -> None:
        rows = [
            {
                "trace_schema": "valewake-action-trace-2",
                "timestamp": "2026-08-27T00:00:00+00:00",
                "event_name": "accepted",
                "job_id": "job_1",
                "turn_id": "turn_1",
                "proposal_id": "proposal_1",
                "action": "water_crops",
            },
            {
                "trace_schema": "valewake-action-trace-2",
                "timestamp": "2026-08-27T00:00:05+00:00",
                "event_name": "target_completed",
                "job_id": "job_1",
                "action": "water_crops",
                "target_allowed": True,
                "mutation_observed": True,
            },
            {
                "trace_schema": "valewake-action-trace-2",
                "timestamp": "2026-08-27T00:00:10+00:00",
                "event_name": "completed",
                "job_id": "job_1",
                "action": "water_crops",
                "completed_targets": 2,
                "failed_targets": 1,
                "baseline_eligible_target_count": 4,
                "max_targets": 3,
                "target_path_attempt_count": 3,
                "target_path_retry_count": 1,
                "return_path_attempt_count": 1,
                "return_path_retry_count": 0,
                "active_duration_ticks": 600,
                "changed_world_keys": ["terrain:1:1", "terrain:2:2"],
                "unexpected_mutation_keys": [],
                "world_state_after": {"terrain:1:1": "watered", "terrain:2:2": "watered"},
                "schedule_restored": True,
                "location_restored": True,
            },
            {
                "trace_schema": "valewake-action-trace-2",
                "timestamp": "2026-08-27T00:00:12+00:00",
                "event_name": "job_postcheck",
                "job_id": "job_1",
                "action": "water_crops",
                "schedule_restored": True,
                "location_restored": True,
                "exact_tile_restored": True,
                "facing_restored": True,
            },
        ]
        report = score_group(rows, "job_id")
        self.assertEqual(1.0, report["productive_task_success_rate"])
        self.assertEqual(0.0, report["no_op_rate"])
        self.assertEqual(0.6667, report["opportunity_recall"])
        self.assertEqual(1.0, report["target_precision"])
        self.assertEqual(0.3333, report["target_path_retry_rate"])
        self.assertEqual(10.0, report["active_duration_p95_seconds"])
        self.assertEqual(1, report["delayed_postcheck_sample"])

    def test_legacy_rows_remain_supported(self) -> None:
        rows = [
            {
                "timestamp": "2026-08-27T00:00:00+00:00",
                "event_name": "accepted",
                "job_id": "legacy",
                "turn_id": "turn",
                "proposal_id": "proposal",
                "action": "clear_weeds",
            },
            {
                "timestamp": "2026-08-27T00:00:03+00:00",
                "event_name": "completed",
                "job_id": "legacy",
                "action": "clear_weeds",
                "completed_targets": 1,
                "failed_targets": 0,
                "schedule_restored": True,
                "location_restored": True,
            },
        ]
        report = score_group(rows, "job_id")
        self.assertEqual(1.0, report["productive_task_success_rate"])
        self.assertIsNone(report["opportunity_recall"])
        self.assertIsNone(report["target_precision"])
        self.assertEqual(0, report["v2_terminal_sample"])


if __name__ == "__main__":
    unittest.main()
