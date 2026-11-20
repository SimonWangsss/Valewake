"""Load local, declarative NPC packs without executing code or opening external paths."""
import json
import logging
import math
from dataclasses import dataclass, field
from pathlib import Path

from stardew_backend.persona import PersonaRegistry


@dataclass
class CustomNpcPacks:
    profiles: dict = field(default_factory=dict)
    chunks: list = field(default_factory=list)
    loaded: list = field(default_factory=list)
    errors: list = field(default_factory=list)


def load_custom_npcs(directory: Path, reserved_ids=()) -> CustomNpcPacks:
    result = CustomNpcPacks()
    used_ids = set(reserved_ids)
    if not directory.exists():
        return result
    allowed_fields = {"display_name", "identity", "personality", "speech_style", "gender", "pronouns",
                      "interests", "relationships", "boundaries"}
    for folder in sorted(directory.iterdir()):
        if not folder.is_dir() or folder.name.startswith((".", "_")):
            continue
        try:
            if folder.is_symlink() or folder.resolve().parent != directory.resolve():
                raise ValueError("Pack folder must be directly inside custom_npcs")
            manifest_path, lore_path = folder / "pack.json", folder / "lore.jsonl"
            if manifest_path.is_symlink() or lore_path.is_symlink():
                raise ValueError("Pack files must not be symbolic links")
            if manifest_path.stat().st_size > 65536:
                raise ValueError("pack.json exceeds 64 KiB")
            pack = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            if not isinstance(pack, dict) or pack.get("schema_version") != 1:
                raise ValueError("Expected pack object with schema_version=1")
            if pack.get("enabled", True) is False:
                continue
            name = pack.get("npc_name")
            if not isinstance(name, str) or not name.strip() or len(name) > 128:
                raise ValueError("npc_name must be the game's internal NPC name")
            key = PersonaRegistry.canonical_name(name)
            if key in result.profiles:
                raise ValueError("Duplicate NPC binding; first valid folder wins")
            profile = pack.get("persona")
            if not isinstance(profile, dict) or set(profile) - allowed_fields:
                raise ValueError("persona contains unknown fields (age/actions/rules are not configurable here)")
            if not isinstance(profile.get("identity"), str) or not profile["identity"].strip():
                raise ValueError("persona.identity is required")
            for field_name, value in profile.items():
                if field_name in {"interests", "relationships", "boundaries"}:
                    if not isinstance(value, list) or len(value) > 40 or any(not isinstance(x, str) or len(x) > 2000 for x in value):
                        raise ValueError(f"Invalid persona list: {field_name}")
                elif not isinstance(value, str) or len(value) > 8000:
                    raise ValueError(f"Invalid persona text: {field_name}")
            chunks, local_ids = [], set()
            if lore_path.exists():
                if lore_path.stat().st_size > 1048576:
                    raise ValueError("lore.jsonl exceeds 1 MiB")
                for number, line in enumerate(lore_path.read_text(encoding="utf-8-sig").splitlines(), 1):
                    if not line.strip():
                        continue
                    item = json.loads(line)
                    if not isinstance(item, dict):
                        raise ValueError(f"Lore line {number} must be an object")
                    chunk_id = item.get("id")
                    if not isinstance(chunk_id, str) or not chunk_id.strip() or len(chunk_id) > 200 or chunk_id in used_ids | local_ids:
                        raise ValueError(f"Invalid or duplicate Lore ID at line {number}")
                    if "npc" in item and PersonaRegistry.canonical_name(str(item["npc"])) != key:
                        raise ValueError(f"Cross-NPC Lore at line {number}")
                    if not isinstance(item.get("text"), str) or not item["text"].strip() or len(item["text"]) > 8000:
                        raise ValueError(f"Invalid Lore text at line {number}")
                    kind = item.get("content_kind", "canonical_fact")
                    if kind not in {"canonical_fact", "canonical_opinion", "experienced_event"}:
                        raise ValueError(f"Invalid content_kind at line {number}")
                    conditions = item.get("conditions", {})
                    if not isinstance(conditions, dict) or set(conditions) - {"min_hearts", "seen_event_ids"}:
                        raise ValueError(f"Invalid conditions at line {number}")
                    hearts = conditions.get("min_hearts", 0)
                    if isinstance(hearts, bool) or not isinstance(hearts, (int, float)) or not math.isfinite(hearts) or not 0 <= hearts <= 14:
                        raise ValueError(f"Invalid min_hearts at line {number}")
                    events = conditions.get("seen_event_ids", [])
                    if not isinstance(events, list) or any(not isinstance(e, (str, int)) for e in events) or (kind == "experienced_event" and not events):
                        raise ValueError(f"Invalid event conditions at line {number}")
                    for list_name in ("tags", "aliases"):
                        values = item.get(list_name, [])
                        if not isinstance(values, list) or len(values) > 40 or any(not isinstance(v, str) or len(v) > 300 for v in values):
                            raise ValueError(f"Invalid {list_name} at line {number}")
                    priority = item.get("priority", 1)
                    if type(priority) is not int or not 0 <= priority <= 3:
                        raise ValueError(f"Invalid priority at line {number}")
                    if not isinstance(item.get("title", ""), str) or not isinstance(item.get("parent_id", ""), str):
                        raise ValueError(f"Invalid title/parent_id at line {number}")
                    # NPC scope and provenance come from the pack, never from a public tag.
                    chunks.append({"schema_version": 2, "id": chunk_id, "npc": key,
                                   "title": item.get("title", chunk_id), "text": item["text"],
                                   "tags": item.get("tags", []), "aliases": item.get("aliases", []),
                                   "content_kind": kind, "conditions": conditions, "priority": priority,
                                   "parent_id": item.get("parent_id", ""), "source": f"custom_npcs/{folder.name}/lore.jsonl",
                                   "review_status": "user_authored_not_verified"})
                    local_ids.add(chunk_id)
                    if len(chunks) > 300:
                        raise ValueError("Maximum 300 Lore records per pack")
            if any(c["parent_id"] and c["parent_id"] not in local_ids for c in chunks):
                raise ValueError("parent_id must refer to Lore in the same pack")
            # Commit a pack only after every record has validated.
            result.profiles[key] = {**profile, "profile_source": f"custom_pack:{folder.name}"}
            result.chunks.extend(chunks)
            used_ids.update(local_ids)
            result.loaded.append({"folder": folder.name, "npc_name": name.strip(), "lore_chunks": len(chunks)})
        except (OSError, ValueError, TypeError) as exc:
            message = f"{folder.name}: {exc}"
            result.errors.append(message)
            logging.getLogger(__name__).warning("Skipping custom NPC pack: %s", message)
    return result
