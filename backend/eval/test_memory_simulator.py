import tempfile
import unittest
from pathlib import Path

from run_memory_simulator import load_scenarios, run_scenarios


class MemorySimulatorTests(unittest.TestCase):
    def test_bundled_timeline_scenarios_pass(self) -> None:
        scenarios = load_scenarios(Path(__file__).with_name("memory_scenarios.json"))
        with tempfile.TemporaryDirectory() as folder:
            report = run_scenarios(scenarios, Path(folder))
        self.assertEqual(report["scenario_count"], report["passed_scenarios"])
        self.assertEqual(30, report["max_game_day"])
        self.assertGreaterEqual(report["assertion_count"], 10)


if __name__ == "__main__":
    unittest.main()
