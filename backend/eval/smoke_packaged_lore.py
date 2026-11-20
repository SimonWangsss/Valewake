"""Launch a packaged backend with mock LLM and temporary memory, then stop it."""
import argparse
import json
import os
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--custom-npc", action="store_true", help="Test a synthetic NPC pack in temporary data")
    args = parser.parse_args()
    data = args.data.resolve()
    expected = json.loads((data / "rag/stardew/corpus.json").read_text(encoding="utf-8"))
    with tempfile.TemporaryDirectory(prefix="valewake-lore-smoke-") as directory:
        root = Path(directory)
        if args.custom_npc:
            copied = root / "data"
            shutil.copytree(data, copied)
            data = copied
            custom = data / "custom_npcs/smoke"
            custom.mkdir(parents=True)
            (custom / "pack.json").write_text(json.dumps({"schema_version": 1, "npc_name": "SmokeCustomNPC",
                "persona": {"identity": "SMOKE_PERSONA_SENTINEL", "speech_style": "Brief and friendly"}}), encoding="utf-8")
            (custom / "lore.jsonl").write_text(json.dumps({"id": "smoke.custom.crystals", "text": "SMOKE_PRIVATE_VIOLET_CRYSTALS"}) + "\n", encoding="utf-8")
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        env = dict(os.environ, LLM_BACKEND="mock", LLM_MODEL="mock", LLM_API_KEY="",
                   LLM_API_BASE="http://127.0.0.1:1", STARDEW_RAG_DIR=str(data / "rag/stardew"),
                   STARDEW_PERSONA_PATH=str(data / "personas/stardew_npcs.json"),
                   STARDEW_MEMORY_PATH=str(root / "memory.json"), STARDEW_TRACE_PATH=str(root / "trace.jsonl"))
        with (root / "server.log").open("w", encoding="utf-8") as log:
            process = subprocess.Popen([str(args.exe.resolve()), "--host", "127.0.0.1", "--port", str(port)],
                                       cwd=root, env=env, stdout=log, stderr=log,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            try:
                base = f"http://127.0.0.1:{port}"
                health = None
                for _ in range(100):
                    if process.poll() is not None:
                        raise RuntimeError("Packaged backend exited: " + (root / "server.log").read_text(encoding="utf-8"))
                    try:
                        with urllib.request.urlopen(base + "/health", timeout=.5) as response:
                            health = json.load(response)
                        break
                    except OSError:
                        time.sleep(.15)
                assert health and health["lore_chunks"] == expected["chunk_count"] + int(args.custom_npc), health
                assert health["llm_backend"] == "mock", health
                observations = []
                for event_ids in ([], ["15"]):
                    state = {"npc": {"name": "Haley"}, "knowledge_state": {"seen_event_ids": event_ids},
                             "npc_perception": {"player": {"hearts": 10}, "time": {"year": 1, "season": "spring", "dayOfMonth": 8}},
                             "npc_events": [{"npc": "Haley", "source": "game_event", "kind": "gift_received", "item_name": "OWN_COFFEE_GIFT_EVENT"},
                                            {"npc": "Penny", "source": "game_event", "kind": "gift_received", "item_name": "OTHER_NPC_GIFT_SECRET"}],
                             "snapshot": {"inventory": "SHOULD_NOT_REACH_MODEL"}}
                    payload = {"player_input": "darkroom photography", "session_id": "smoke:Haley",
                               "game_state": state, "debug": True}
                    request = urllib.request.Request(base + "/chat", data=json.dumps(payload).encode(),
                                                     headers={"Content-Type": "application/json"})
                    with urllib.request.urlopen(request, timeout=10) as response:
                        chat = json.load(response)
                    assert "SHOULD_NOT_REACH_MODEL" not in chat["debug_prompt"]
                    assert "OWN_COFFEE_GIFT_EVENT" in chat["debug_prompt"]
                    assert "OTHER_NPC_GIFT_SECRET" not in chat["debug_prompt"]
                    traces = [json.loads(line) for line in (root / "trace.jsonl").read_text(encoding="utf-8").splitlines()]
                    trace = traces[-1]
                    assert trace["knowledge_corpus_version"] == expected["version"]
                    contains_event = "darkroom_event" in "\n".join(chat["retrieved_lore"])
                    assert contains_event == bool(event_ids), chat["retrieved_lore"]
                    observations.append({"seen_event_ids": event_ids, "event_knowledge_present": contains_event,
                                         "own_gift_event_visible": True, "other_npc_gift_excluded": True,
                                         "full_snapshot_excluded": True, "trace_corpus_version": trace["knowledge_corpus_version"]})
                if args.custom_npc:
                    assert any(p["npc_name"] == "SmokeCustomNPC" for p in health["custom_npc_packs"])
                    assert not health["custom_npc_errors"], health
                    for name in ("SmokeCustomNPC", "Abigail"):
                        request = urllib.request.Request(base + "/chat", data=json.dumps({
                            "player_input": "SMOKE_PRIVATE_VIOLET_CRYSTALS", "session_id": f"smoke:{name}",
                            "game_state": {"npc": {"name": name, "age_group": "adult"}}, "debug": True}).encode(),
                            headers={"Content-Type": "application/json"})
                        with urllib.request.urlopen(request, timeout=10) as response:
                            chat = json.load(response)
                        assert ("SMOKE_PERSONA_SENTINEL" in chat["debug_prompt"]) == (name == "SmokeCustomNPC")
                        assert any("SMOKE_PRIVATE_VIOLET_CRYSTALS" in text for text in chat["retrieved_lore"]) == (name == "SmokeCustomNPC")
                report = {"passed": True, "llm": "mock", "chunks": health["lore_chunks"],
                          "custom_pack_tested": args.custom_npc,
                          "observations": observations, "real_player_data_used": False, "paid_api_calls": 0}
            finally:
                if process.poll() is None:
                    if os.name == "nt":
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
                    else:
                        process.terminate()
                process.wait(timeout=10)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
