import re
from typing import Any
from uuid import uuid4


SUPPORTED_ACTIONS = {
    "water_crops", "clear_weeds", "chop_trees", "join_mine_expedition", "defend_player",
    "mine_target", "mine_nearby", "mine_expedition", "schedule_meeting",
}

FARM_ACTIONS = {"water_crops", "clear_weeds", "chop_trees"}
MINE_ACTIONS = {"join_mine_expedition", "defend_player", "mine_target", "mine_nearby", "mine_expedition"}
MEETING_ACTIONS = {"schedule_meeting"}
UNBOUNDED_ACTIONS = {
    "water_crops", "clear_weeds", "join_mine_expedition", "defend_player",
    "mine_nearby", "mine_expedition",
}


def action_eligibility(action: str, game_state: dict[str, Any]) -> dict[str, Any]:
    """Mirror the deterministic SMAPI gates that are knowable before generation."""
    if not action:
        return {"requested_action": "", "eligible": False, "reason_code": "no_action"}

    rules = game_state.get("action_rules") or {}
    snapshot = game_state.get("npc_perception") or game_state.get("snapshot") or {}
    player = snapshot.get("player") or {}
    relationship = game_state.get("agent_relationship") or {}
    try:
        hearts = int(player.get("hearts", 0) or 0)
    except (TypeError, ValueError):
        hearts = 0
    try:
        trust = int(rules.get("current_trust", relationship.get("trust", 0)) or 0)
    except (TypeError, ValueError):
        trust = 0

    is_mine = action in MINE_ACTIONS
    is_meeting = action in MEETING_ACTIONS
    minimum_hearts = int(rules.get(
        "minimum_expedition_hearts" if is_mine else "minimum_farm_hearts",
        4 if is_mine else 2,
    ) or 0)
    minimum_trust = int(rules.get("minimum_trust", 0) or 0)
    current_time = int(rules.get("current_time", 0) or 0)
    end_time = int(rules.get(
        "expedition_end_time" if is_mine else "farm_end_time",
        2300 if is_mine else 2200,
    ) or 0)

    enabled_key = f"{action}_enabled"
    action_enabled = rules.get(enabled_key, True)
    if is_mine:
        action_enabled = bool(rules.get("mine_expeditions_enabled", True))

    reason_code = ""
    if not bool(rules.get("enabled", True)) or not action_enabled:
        reason_code = "action_disabled"
    elif not bool(rules.get("is_host", True)):
        reason_code = "host_only"
    elif bool(rules.get("event_active", False)):
        reason_code = "event_active"
    elif bool(rules.get("npc_is_child", False)):
        reason_code = "child_npc"
    elif not is_meeting and current_time >= end_time:
        reason_code = "too_late"
    elif hearts < minimum_hearts:
        reason_code = "insufficient_hearts"
    elif trust < minimum_trust:
        reason_code = "insufficient_trust"

    return {
        "requested_action": action,
        "eligible": not reason_code,
        "reason_code": reason_code,
        "current_hearts": hearts,
        "minimum_hearts": minimum_hearts,
        "current_trust": trust,
        "minimum_trust": minimum_trust,
        "request_attempt_limit": max(1, int(rules.get("request_attempt_limit", 3) or 3)),
    }


def forced_action_proposal(action: str, player_input: str, disposition: str, reason: str) -> dict[str, Any]:
    return normalize_action_proposal({
        "action": action,
        "disposition": disposition,
        "confidence": 0.95,
        "reason": reason,
        "evidence": player_input,
    }, player_input) or {}

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
    "schedule_meeting": "schedule_meeting",
    "meet_up": "schedule_meeting",
    "meeting": "schedule_meeting",
    "appointment": "schedule_meeting",
    "\u89c1\u9762": "schedule_meeting",
    "\u7ea6\u4f1a": "schedule_meeting",
}


def requested_action(player_input: str) -> str:
    lowered = (player_input or "").strip().lower()
    if re.search(r"是不是你|是你.{0,12}(?:吗|的)|did you|have you", lowered) and not re.search(r"请你|你能|可以.{0,5}帮我|please|could you", lowered):
        return ""
    # A later invitation or third-person report is not an instruction to this NPC now.
    if re.search(r"(?:再|以后|之后).{0,4}(?:邀请|约你)|(?:invite|ask) you (?:later|after)", lowered):
        return ""
    addressed_now = bool(re.search(r"(?:你.{0,5}(?:陪我|帮我|一起)|(?:陪我|帮我|跟我|和我|跟你|和你)|can you|could you|join me|with you)", lowered))
    if not addressed_now and re.search(r"(?:我和|我跟|我很喜欢|我与).{1,20}(?:约|一起)|(?:he|she|they|we).{0,20}(?:agreed|planned)|i (?:am|was).{0,15}with ", lowered):
        return ""
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
    )) or bool(re.search(r"我.{0,6}已经|已经.{0,6}(?:了|过)", lowered))
    # 玩家表达"自己打算/想/要去做"而不是请 NPC 帮忙——这类个人意图不应触发动作请求。
    # 例："我想在附近砍砍树"（自己要砍）vs "帮我砍树"（请 NPC 砍）。
    self_intent = any(marker in lowered for marker in (
        "i'm going to", "i plan to", "i want to", "i'll ", "myself", "on my own",
        "\u6253\u7b97", "\u51c6\u5907", "\u6211\u60f3", "\u6211\u8981", "\u6211\u81ea\u5df1", "\u81ea\u5df1\u6765",
    ))
    # 双向意图（见面/一起做）——不是"自己一个人干"，不应被上面的个人意图拦截。
    mutual_intent = any(marker in lowered for marker in (
        "together", "with you", "let's meet", "meet me",
        "\u4e00\u8d77", "\u89c1\u9762", "\u548c\u4f60", "\u8ddf\u4f60", "\u627e\u4f60", "\u6211\u4eec", "\u966a\u6211",
    ))
    if (discussion_marker or past_statement or (self_intent and not mutual_intent)) and not explicit_request:
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
    )) or re.search(r"浇.{0,3}水", lowered):
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
    meeting_agreement = (
        "\u8bf4\u5b9a", "\u7ea6\u597d", "\u4e0d\u89c1\u4e0d\u6563", "\u5230\u65f6\u5019\u89c1",
        "\u4e00\u8a00\u4e3a\u5b9a",
        "it's a date", "let's meet", "meet me", "see you then", "deal",
    )
    meeting_time = (
        "\u660e\u5929", "\u540e\u5929", "\u4eca\u665a", "\u660e\u665a", "\u65e9\u4e0a", "\u65e9\u6668",
        "\u508d\u665a", "\u665a\u4e0a", "\u4e0b\u5348", "\u4e2d\u5348", "\u70b9", "\u65f6",
        "tomorrow", "tonight", "morning", "evening", "afternoon", "noon",
    )
    meeting_place = (
        "\u6d77\u8fb9", "\u6d77\u6ee9", "\u5c71\u4e0a", "\u68ee\u6797", "\u9547\u4e0a", "\u519c\u573a",
        "\u77ff\u6d1e", "\u9152\u9986", "\u9152\u5427", "\u6742\u8d27\u5e97", "\u5e97",
        "beach", "mountain", "forest", "town", "farm", "mine", "saloon",
    )
    meeting_verb = (
        "\u89c1", "\u627e", "\u7b49", "\u7ea6", "\u4e00\u8d77", "\u78b0\u5934", "\u78b0\u9762",
        "meet", "together",
        "开始钓鱼吧", "钓鱼吧", "散步吧",
    )
    if any(marker in lowered for marker in meeting_agreement):
        return "schedule_meeting"
    has_meeting_time = any(marker in lowered for marker in meeting_time) or bool(re.search(r"\d{1,2}[:：]\d{2}", lowered))
    if (has_meeting_time
            and "\u4e00\u8d77" in lowered):
        return "schedule_meeting"
    if (has_meeting_time
            and any(marker in lowered for marker in meeting_place)
            and any(marker in lowered for marker in meeting_verb)):
        return "schedule_meeting"
    return ""


def contextual_requested_action(player_input: str, history: list[dict]) -> str:
    direct = requested_action(player_input)
    if direct:
        return direct
    recent = history[-6:]
    # A short assent is actionable only immediately after a concrete meeting suggestion.
    if not recent or recent[-1].get("role") != "assistant":
        return ""
    last = str(recent[-1].get("content", ""))
    if not any(m.get("role") == "user" and requested_action(str(m.get("content", ""))) == "schedule_meeting" for m in recent):
        return ""
    if not re.search(r"见|碰头|碰面|约|meet", last, re.I):
        return ""
    if re.search(r"算了|(?<!要)不去|不要|取消|不约|改天再说|不用|不见|不是|之前|刚才|昨天|记得|见过|上次|为什么|如果|假如|don't|cancel", player_input, re.I):
        return ""
    assent = bool(re.fullmatch(r"(?:好啊?|好的|可以|行啊?|嗯|那就这样|就这么定了|没问题|yes|sure|okay|ok)[啊呀吧呢，,。.!！\s]*(?:好啊?)*", player_input.strip(), re.I))
    adjustment = bool(re.search(r"见|碰头|碰面|改到|换到|要不|几点|半|[:：]|\d点|[一二三四五六七八九十]点", player_input))
    concrete = bool(re.search(r"[\d一二三四五六七八九十两]+点|\d{1,2}[:：]\d{2}", last))
    return "schedule_meeting" if adjustment or assent and concrete else ""


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
    if action in UNBOUNDED_ACTIONS:
        maximum = 0
    elif action == "mine_target":
        maximum = 1
    elif action == "chop_trees":
        maximum = 3
    else:
        default_maximum = 10
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

    if action == "schedule_meeting":
        try:
            day_offset = max(0, min(7, int(parameters.get("day_offset", 0))))
        except (TypeError, ValueError):
            day_offset = 0
        meeting_location = str(parameters.get("location") or "").strip()
        meeting_time = _parse_meeting_time(
            parameters.get("time_of_day") or parameters.get("time"),
            player_input,
        )
        meeting_topic = str(parameters.get("topic") or "").strip()[:120]
        if not meeting_topic:
            meeting_topic = player_input.strip()[:120]
        return {
            "proposal_id": str(value.get("proposal_id") or f"proposal_{uuid4().hex[:12]}"),
            "intent": "request_help",
            "action": action,
            "disposition": disposition,
            "parameters": {
                "location": meeting_location,
                "time_of_day": meeting_time,
                "day_offset": 2 if "后天" in player_input or "day after tomorrow" in player_input.lower() else 1 if "明天" in player_input or "明晚" in player_input or "tomorrow" in player_input.lower() else 0 if "今天" in player_input or "今晚" in player_input or "today" in player_input.lower() else day_offset,
                "topic": meeting_topic,
            },
            "confidence": confidence,
            "reason": str(value.get("reason") or "")[:240],
            "evidence": evidence,
            "requires_confirmation": True,
        }

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


def _requested_clock(player_input: str):
    clocks = list(re.finditer(r"(\d{1,2})[:：](\d{2})|([\d一二两三四五六七八九十]{1,3})点(半|[\d]{1,2}分?)?", player_input))
    viable = []
    for match in clocks:
        before = re.split(r"[，,。；;!?！？]", player_input[:match.start()])[-1][-8:]
        after = re.split(r"[，,。；;!?！？]", player_input[match.end():])[0][:16]
        if re.search(r"不要|不选|不是|别约|别选", before) or re.search(r"太晚|太早|有点晚|有点早|不行|不方便|没空|不合适|必须回|得回|要回", after):
            continue
        viable.append(match)
    return viable[-1] if viable else None


def _parse_meeting_time(raw_time: Any, player_input: str) -> int:
    # Explicit player clock wins over model guesses and broad words like 'afternoon'.
    explicit = _requested_clock(player_input)
    if explicit:
        if explicit.group(1):
            hour, minute = int(explicit.group(1)), int(explicit.group(2))
        else:
            number = explicit.group(3)
            digits = {"一": 1, "二": 2, "两": 2, "三": 3, "四": 4, "五": 5, "六": 6, "七": 7, "八": 8, "九": 9}
            if number.isdigit(): hour = int(number)
            elif "十" in number:
                left, right = number.split("十", 1)
                hour = digits.get(left, 1) * 10 + digits.get(right, 0)
            else: hour = digits.get(number, -1)
            minutes = explicit.group(4) or "0"
            minute = 30 if minutes == "半" else int(minutes.rstrip("分"))
        periods = list(re.finditer(r"下午|傍晚|晚上|今晚|明晚|中午|早上|上午|\bam\b|\bpm\b", player_input[:explicit.start()], re.I))
        if periods and periods[-1].group(0).lower() in {"下午", "傍晚", "晚上", "今晚", "明晚", "pm"} and 0 < hour < 12:
            hour += 12
        return hour * 100 + minute if 0 <= minute < 60 else -1
    if isinstance(raw_time, int) and not isinstance(raw_time, bool):
        return raw_time
    text = str(raw_time or "").strip() or (player_input or "")
    lowered = text.lower()
    clock = re.search(r"(\d{1,2})[:：](\d{2})", text)
    if clock:
        hour = int(clock.group(1))
        minute = int(clock.group(2))
        if 0 <= hour <= 23 and 0 <= minute <= 59:
            return _clamp_meeting_time(hour * 100 + minute)
    compact = re.search(r"(?<![\d])([01]?\d)([0-5]\d)(?![\d])", text)
    if compact:
        hour = int(compact.group(1))
        minute = int(compact.group(2))
        if 0 <= hour <= 23 and 0 <= minute <= 59 and (hour >= 6 or hour == 0):
            return _clamp_meeting_time(hour * 100 + minute)
    named = (
        ("\u65e9\u4e0a", 800), ("\u65e9\u6668", 800), ("\u6e05\u6668", 800),
        ("\u4e0a\u5348", 1000), ("\u4e2d\u5348", 1200), ("\u4e0b\u5348", 1500),
        ("\u508d\u665a", 1800), ("\u665a\u4e0a", 1900),
        ("morning", 800), ("noon", 1200), ("afternoon", 1500),
        ("evening", 1800), ("night", 2000),
    )
    for marker, value in named:
        if marker in lowered:
            return _clamp_meeting_time(value)
    zh_digits = {"\u4e00": 1, "\u4e8c": 2, "\u4e09": 3, "\u56db": 4, "\u4e94": 5,
                 "\u516d": 6, "\u4e03": 7, "\u516b": 8, "\u4e5d": 9, "\u5341": 10}
    zh_clock = re.search(r"([\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341]{1,3})\u70b9", text)
    if zh_clock:
        digits = zh_clock.group(1)
        if digits == "\u5341":
            return 1000
        value = 0
        for char in digits:
            value += zh_digits.get(char, 0)
        if 0 < value <= 12:
            return _clamp_meeting_time(value * 100)
    return 1800


def _clamp_meeting_time(value: int) -> int:
    return max(800, min(2200, value))
