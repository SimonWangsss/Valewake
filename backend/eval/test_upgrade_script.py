"""Exercise the shipped migration script with disposable installations, never player data."""
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "scripts/Upgrade-Valewake.ps1"


@unittest.skipUnless(os.name == "nt" and shutil.which("powershell.exe"), "Windows migration script")
class UpgradeScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.old, self.new = self.root / "old", self.root / "Mods/Valewake"
        for folder in (self.old, self.new):
            self.write(folder / "manifest.json", {"UniqueID": "Simon.Valewake", "Version": "1.1.0"})
        self.write(self.old / "config.json", {"LlmApiKey": "TEST_ONLY_SECRET", "LlmModel": "old-model",
                                              "BackendExecutablePath": "Z:/wrong.exe", "TestMode": True})
        self.write(self.new / "config.json", {"LlmApiKey": "", "LlmModel": "new-model",
                                              "BackendExecutablePath": "Backend/ValewakeBackend.exe", "TestMode": False})
        self.write(self.old / "data/memory/player_memory.json", {"working": True})
        self.write(self.old / "data/memory/player_memory.committed.json", {"committed": True})
        self.write(self.old / "data/traces/agent_trace.jsonl", "old trace")
        self.write(self.old / "data/rag/knowledge.json", "OLD KNOWLEDGE")
        self.write(self.new / "data/rag/knowledge.json", "NEW KNOWLEDGE")

    @staticmethod
    def write(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value), encoding="utf-8")

    def run_script(self, *args):
        # Simulate a closed game independent of whatever the test host is doing.
        quote = lambda p: "'" + str(p).replace("'", "''") + "'"
        command = ("function global:Get-Process { @() }; & " + quote(SCRIPT) +
                   " -OldModPath " + quote(self.old) + " -NewModPath " + quote(self.new) + " " + " ".join(args))
        return subprocess.run(["powershell.exe", "-NoProfile", "-Command", command], capture_output=True,
                              text=True, encoding="utf-8", errors="replace", timeout=30)

    def test_migrates_data_and_only_allowed_config(self):
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual((self.old / "data/memory/player_memory.committed.json").read_bytes(),
                         (self.new / "data/memory/player_memory.committed.json").read_bytes())
        self.assertTrue((self.new / "data/traces/agent_trace.jsonl").exists())
        self.assertIn("NEW KNOWLEDGE", (self.new / "data/rag/knowledge.json").read_text())
        config = json.loads((self.new / "config.json").read_text(encoding="utf-8-sig"))
        self.assertEqual("TEST_ONLY_SECRET", config["LlmApiKey"])
        self.assertFalse(config["TestMode"])
        self.assertEqual("Backend/ValewakeBackend.exe", config["BackendExecutablePath"])
        self.assertNotIn("TEST_ONLY_SECRET", result.stdout + result.stderr)
        self.assertTrue(list((self.root / "ValewakeUpgradeBackups").glob("*/migration.json")))
        self.assertTrue((self.old / "data/memory/player_memory.json").exists())

    def test_preview_does_not_write(self):
        before = (self.new / "config.json").read_bytes()
        result = self.run_script("-WhatIf")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(before, (self.new / "config.json").read_bytes())
        self.assertFalse((self.new / "data/memory").exists())
        self.assertFalse((self.root / "ValewakeUpgradeBackups").exists())

    def test_destination_conflict_is_preflight_failure(self):
        self.write(self.new / "data/memory/player_memory.json", {"newer": True})
        result = self.run_script()
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.new / "data/memory/player_memory.committed.json").exists())
        self.assertIn("newer", (self.new / "data/memory/player_memory.json").read_text())

    def test_legacy_backend_data_is_supported(self):
        (self.old / "Backend").mkdir()
        shutil.move(str(self.old / "data"), str(self.old / "Backend/data"))
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue((self.new / "data/memory/player_memory.json").exists())

    def test_split_sources_are_not_guessed(self):
        self.write(self.old / "Backend/data/memory/player_memory.json", {"other": True})
        result = self.run_script()
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.new / "data/memory").exists())

    def test_custom_packs_keep_new_template(self):
        self.write(self.old / "data/custom_npcs/mine/pack.json", {"custom": True})
        self.write(self.old / "data/custom_npcs/_template/pack.json", {"old": True})
        self.write(self.new / "data/custom_npcs/_template/pack.json", {"new": True})
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue((self.new / "data/custom_npcs/mine/pack.json").exists())
        self.assertIn("new", (self.new / "data/custom_npcs/_template/pack.json").read_text())

    def test_rerun_identical_data_is_safe(self):
        self.assertEqual(0, self.run_script().returncode)
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_no_merging_new_trace_timeline(self):
        self.write(self.new / "data/traces/new_trace.jsonl", "new session")
        result = self.run_script()
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.new / "data/memory").exists())

    def test_same_directory_is_rejected(self):
        self.new = self.old
        before = (self.old / "config.json").read_bytes()
        self.assertNotEqual(0, self.run_script().returncode)
        self.assertEqual(before, (self.old / "config.json").read_bytes())

    def test_wrong_mod_is_rejected(self):
        self.write(self.new / "manifest.json", {"UniqueID": "Other.Mod"})
        self.assertNotEqual(0, self.run_script().returncode)
        self.assertFalse((self.new / "data/memory").exists())

    def test_select_data_inside_mod(self):
        self.old = self.old / "data"
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        config = json.loads((self.new / "config.json").read_text(encoding="utf-8-sig"))
        self.assertEqual("TEST_ONLY_SECRET", config["LlmApiKey"])
        self.assertTrue((self.new / "data/memory/player_memory.json").exists())

    def test_select_outer_extracted_directory(self):
        outer = self.root / "old-package"
        outer.mkdir()
        shutil.move(str(self.old), str(outer / "Valewake"))
        self.old = outer
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue((self.new / "data/memory/player_memory.json").exists())

    def test_standalone_data_backup(self):
        data = self.root / "data"
        shutil.copytree(self.old / "data", data)
        self.old = data
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        config = json.loads((self.new / "config.json").read_text(encoding="utf-8-sig"))
        self.assertEqual("", config["LlmApiKey"])
        self.assertTrue((self.new / "data/memory/player_memory.committed.json").exists())

    def test_legacy_backend_data_selection(self):
        (self.old / "Backend").mkdir()
        shutil.move(str(self.old / "data"), str(self.old / "Backend/data"))
        self.old = self.old / "Backend/data"
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue((self.new / "data/traces/agent_trace.jsonl").exists())

    def test_ambiguous_outer_directory_is_rejected(self):
        self.old = self.root
        self.write(self.root / "another/manifest.json", {"UniqueID": "Simon.Valewake"})
        result = self.run_script()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Multiple Valewake folders", result.stderr)
        self.assertFalse((self.new / "data/memory").exists())

    def test_missing_manifest_has_actionable_error(self):
        self.old = self.root / "not-a-mod"
        self.old.mkdir()
        result = self.run_script()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("No Valewake installation found", result.stderr)

    def test_new_data_selection_resolves_existing_installation(self):
        self.new = self.new / "data"
        result = self.run_script()
        # A data folder inside a full installation resolves to that installation.
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_standalone_data_is_not_a_valid_destination(self):
        self.new = self.root / "target/data"
        (self.new / "memory").mkdir(parents=True)
        result = self.run_script()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("No Valewake installation found", result.stderr)

    def test_file_launch_defaults_to_script_folder(self):
        script = self.new / "Upgrade-Valewake.ps1"
        shutil.copy2(SCRIPT, script)
        result = subprocess.run(["powershell.exe", "-NoProfile", "-File", str(script),
                                 "-OldModPath", str(self.old / "data"), "-WhatIf"],
                                capture_output=True, text=True, errors="replace", timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn(str(self.new), result.stdout)
        self.assertFalse((self.new / "data/memory").exists())
