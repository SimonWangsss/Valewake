from typing import Any
from uuid import uuid4


SUPPORTED_ACTIONS = {
    "water_crops", "clear_weeds", "chop_trees", "join_mine_expedition", "defend_player",
    "mine_target", "mine_nearby", "mine_expedition",
}

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
    "chop_trees": "chop_trees",
    "chop_tree": "chop_trees",
    "cut_trees": "chop_trees",
    "logging": "chop_trees",
    "\u780d\u6811": "chop_trees",
    "\u4f10\u6728": "chop_trees",
    "join_mine_expedition": "join_mine_expedition",
    "follow_to_mines": "join_mine_expedition",
    "defend_player": "defend_player",
    "guard_player": "defend_player",
    "mine_target": "mine_target",
    "mine_nearby": "mine_nearby",
    "mine_expedition": "mine_expedition",
}


def requested_action(player_input: str) -> str:
    lowered = (player_input or "").strip().lower()
    explicit_request = any(marker in lowered for marker in (
        "can you", "could you", "will you", "please", "help me", "for me",
        "\u5e2e\u6211", "\u53ef\u4ee5", "\u80fd\u4e0d\u80fd", "\u80fd\u5e2e", "\u8bf7", "\u9ebb\u70e6", "\u5427", "\u597d\u5417",
    ))
    discussion_marker = any(marker in lowered for marker in (
        "do you enjoy", "do you like", "what do you think", "why ",
        "\u4f60\u559c\u6b22", "\u4f60\u600e\u4e48\u770b", "\u4f60\u89c9\u5f97", "\u4e3a\u4ec0\u4e48",
    ))
    past_statement = any(marker in lowered for marker in (
        "i already", "i went", "yesterday", "this morning",
        "\u6211\u5df2\u7ecf", "\u6211\u521a\u521a", "\u6211\u6628\u5929", "\u4eca\u65e9\u6211",
    ))
    if (discussion_marker or past_statement) and not explicit_request:
        return ""
    if any(marker in lowered for marker in (
        "mine expedition", "mining expedition", "look for iron", "prioritize iron",
        "\u4e0b\u77ff\u63a2\u9669", "\u77ff\u6d1e\u63a2\u9669", "\u4f18\u5148\u627e\u94c1", "\u4f18\u5148\u6316\u94c1",
    )):
        return "mine_expedition"
    if any(marker in lowered for marker in (
        "mine this rock", "break this rock", "mine this node", "break this ore node",
        "\u6316\u8fd9\u5757", "\u6316\u8fd9\u4e2a", "\u6572\u8fd9\u5757", "\u5f00\u91c7\u8fd9\u4e2a",
    )):
        return "mine_target"
    if (("\u8fd9\u4e2a\u77ff" in lowered or "\u8fd9\u5757\u77ff" in lowered) and
            any(verb in lowered for verb in ("\u6316", "\u6572", "\u5f00\u91c7"))):
        return "mine_target"
    if any(marker in lowered for marker in (
        "mine nearby", "break nearby rocks", "break a few nearby rocks", "mine nearby ore",
        "\u6316\u9644\u8fd1", "\u6572\u9644\u8fd1", "\u5f00\u91c7\u9644\u8fd1",
    )):
        return "mine_nearby"
    if (("\u9644\u8fd1" in lowered or "\u65c1\u8fb9" in lowered) and
            any(verb in lowered for verb in ("\u6316", "\u6572", "\u5f00\u91c7")) and
            any(noun in lowered for noun in ("\u77ff", "\u77f3\u5934", "\u77ff\u77f3"))):
        return "mine_nearby"
    if any(marker in lowered for marker in (
        "defend me", "protect me", "fight monsters", "guard me",
        "\u4fdd\u62a4\u6211", "\u5e2e\u6211\u6253\u602a", "\u5e2e\u6211\u6218\u6597", "\u5b88\u7740\u6211",
    )):
        return "defend_player"
    if any(marker in lowered for marker in (
        "come to the mines", "join me in the mines", "follow me to the mines",
        "\u966a\u6211\u4e0b\u77ff", "\u8ddf\u6211\u4e0b\u77ff", "\u4e00\u8d77\u4e0b\u77ff", "\u548c\u6211\u53bb\u77ff\u6d1e",
    )):
        return "join_mine_expedition"
    if any(marker in lowered for marker in (
        "water the crop", "water my crop", "water the field",
        "help me water", "\u6d47\u6c34", "\u6d47\u5730",
        "\u7ed9\u7530\u5730", "\u7ed9\u4f5c\u7269",
    )):
        return "water_crops"
    if any(marker in lowered for marker in (
        "chop trees", "chop a tree", "cut down trees", "cut down a tree", "help me log",
        "\u5e2e\u6211\u780d\u6811", "\u780d\u51e0\u68f5\u6811", "\u780d\u6389\u8fd9\u4e9b\u6811", "\u5e2e\u6211\u4f10\u6728",
    )) or (
        ("tree" in lowered and any(verb in lowered for verb in ("chop", "cut down", "logging")))
        or ("\u6811" in lowered and any(verb in lowered for verb in ("\u780d", "\u4f10\u6728")))
    ):
        return "chop_trees"
    if any(marker in lowered for marker in (
        "clear weed", "remove weed", "remove the weed", "pull weed", "help me weed",
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
    default_maximum = 1 if action == "mine_target" else 10
    try:
        maximum = max(1, min(10, int(parameters.get("max_targets", default_maximum))))
    except (TypeError, ValueError):
        maximum = default_maximum

    priority = str(parameters.get("resource_priority") or "any").strip().lower()
    if "\u94c1" in player_input or "iron" in player_input.lower():
        priority = "iron"
    elif "\u94dc" in player_input or "copper" in player_input.lower():
        priority = "copper"
    elif "\u91d1" in player_input or "gold" in player_input.lower():
        priority = "gold"
    elif "\u94f1" in player_input or "iridium" in player_input.lower():
        priority = "iridium"
    if priority not in {"any", "stone", "copper", "iron", "gold", "iridium"}:
        priority = "any"

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
            "location": "Mines" if action.startswith("mine") or action in {"join_mine_expedition", "defend_player"} else "Farm",
            "area": "cursor" if action == "mine_target" else "near_player",
            "max_targets": maximum,
            "resource_priority": priority,
        },
        "confidence": confidence,
        "reason": str(value.get("reason") or "")[:240],
        "evidence": evidence,
        "requires_confirmation": True,
    }
