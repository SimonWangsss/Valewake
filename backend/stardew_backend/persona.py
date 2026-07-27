import json
from pathlib import Path
from typing import Any


DEFAULT_PROFILE = {
    "age_group": "adult",
    "identity": "A resident of Stardew Valley.",
    "personality": "Respond as an individual resident, not as a generic assistant.",
    "interests": [],
    "relationships": [],
    "speech_style": "Natural, concise, and grounded in the current relationship.",
    "boundaries": [
        "Do not invent private knowledge about other residents.",
        "Do not claim to see game state outside the supplied perception.",
    ],
}


class PersonaRegistry:
    def __init__(self, path: Path):
        self.path = path
        self.profiles = self._load()

    def _load(self) -> dict[str, dict[str, Any]]:
        if not self.path.exists():
            return {}
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"Invalid NPC persona registry: {self.path}") from exc
        if not isinstance(data, dict):
            raise ValueError(f"NPC persona registry must be an object: {self.path}")
        return {
            str(name).lower(): profile
            for name, profile in data.items()
            if isinstance(profile, dict)
        }

    def get(
        self,
        npc_name: str,
        display_name: str = "",
        runtime_age_group: str = "",
    ) -> dict[str, Any]:
        resolved_name = (npc_name or display_name or "Villager").strip()
        stored = self.profiles.get(resolved_name.lower(), {})
        stored_boundaries = stored.get("boundaries")
        boundaries = list(DEFAULT_PROFILE["boundaries"])
        if isinstance(stored_boundaries, list):
            boundaries.extend(
                item for item in stored_boundaries if item not in boundaries
            )
        age_group = (
            runtime_age_group
            if runtime_age_group in {"child", "teen", "adult", "elder"}
            else stored.get("age_group", DEFAULT_PROFILE["age_group"])
        )
        profile = {
            **DEFAULT_PROFILE,
            **stored,
            "age_group": age_group,
            "boundaries": boundaries,
            "name": resolved_name,
            "display_name": display_name or stored.get("display_name") or resolved_name,
            "profile_source": "curated" if stored else "generic_fallback",
        }
        for key in ("interests", "relationships"):
            value = profile.get(key)
            profile[key] = value if isinstance(value, list) else []
        return profile

    @property
    def curated_count(self) -> int:
        return len(self.profiles)
