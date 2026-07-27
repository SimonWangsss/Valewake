from typing import Any
from uuid import uuid4


SUPPORTED_ACTIONS = {"water_crops", "clear_weeds"}

ACTION_ALIASES = {
    "water": "water_crops",
    "water_crop": "water_crops",
    "water_crops": "water_crops",
    "watering": "water_crops",
    "\u6d47\u6c34": "water_crops",
    "\u6d47\u5730": "water_crops",
    "clear_weeds": "clear_weeds",
    "remove_weeds": "clear_weeds",
    "weed": "clear_weeds",
    "weeding": "clear_weeds",
    "\u9664\u8349": "clear_weeds",
    "\u6e05\u7406\u6742\u8349": "clear_weeds",
}


def requested_action(player_input: str) -> str:
    lowered = (player_input or "").strip().lower()
    if any(marker in lowered for marker in (
        "water the crop", "water my crop", "water the field",
        "help me water", "\u6d47\u6c34", "\u6d47\u5730",
        "\u7ed9\u7530\u5730", "\u7ed9\u4f5c\u7269",
    )):
        return "water_crops"
    if any(marker in lowered for marker in (
        "clear weed", "remove weed", "pull weed", "help me weed",
        "\u9664\u8349", "\u6e05\u7406\u6742\u8349", "\u62d4\u8349",
    )) or (
        "\u6742\u8349" in lowered
        and any(
            verb in lowered
            for verb in ("\u6e05\u7406", "\u62d4", "\u9664", "\u5e2e")
        )
    ):
        return "clear_weeds"
    return ""


def normalize_action_proposal(
    value: Any,
    player_input: str,
) -> dict[str, Any] | None:
    requested = requested_action(player_input)
    if not isinstance(value, dict):
        return None

    raw_action = str(value.get("action") or requested).strip().lower()
    action = ACTION_ALIASES.get(raw_action, raw_action)
    if action not in SUPPORTED_ACTIONS or (requested and action != requested):
        return None

    disposition = str(
        value.get("disposition")
        or value.get("npc_response")
        or "refuse"
    ).strip().lower()
    if disposition not in {"accept", "refuse", "negotiate"}:
        disposition = "refuse"

    try:
        confidence = max(0.0, min(1.0, float(value.get("confidence", 0.0))))
    except (TypeError, ValueError):
        confidence = 0.0

    parameters = value.get("parameters")
    if not isinstance(parameters, dict):
        parameters = {}
    try:
        maximum = max(1, min(10, int(parameters.get("max_targets", 10))))
    except (TypeError, ValueError):
        maximum = 10

    evidence = str(value.get("evidence") or "")[:160]
    if disposition == "accept" and requested:
        if not evidence or evidence.lower() not in player_input.lower():
            evidence = player_input.strip()[:160]
        confidence = max(confidence, 0.8)

    return {
        "proposal_id": str(value.get("proposal_id") or f"proposal_{uuid4().hex[:12]}"),
        "intent": "request_help",
        "action": action,
        "disposition": disposition,
        "parameters": {
            "location": "Farm",
            "area": "near_player",
            "max_targets": maximum,
        },
        "confidence": confidence,
        "reason": str(value.get("reason") or "")[:240],
        "evidence": evidence,
        "requires_confirmation": True,
    }
