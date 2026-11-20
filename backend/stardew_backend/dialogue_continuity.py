"""Grounded continuity without turning prior model prose into character facts."""
import json
import re
from stardew_backend.action_policy import _parse_meeting_time, _requested_clock, requested_action


def compact_episodes(episodes: list[dict], history: list[dict]) -> list[dict]:
    visible = {str(m.get("content", "")).strip() for m in history if m.get("role") == "user"}
    result = []
    for episode in episodes:
        utterance = str(episode.get("player_input", "")).strip()
        if not utterance or utterance in visible:
            continue
        visible.add(utterance)
        result.append({"game_day": episode.get("game_day"), "player_said": utterance[:240],
                       "discussed_action": episode.get("requested_action", ""),
                       "proposal_disposition": episode.get("action_disposition", ""),
                       "execution_verified": False})
    return result[-6:]


def budget_json(items: list[dict], budget: int = 4000) -> str:
    selected = list(items)
    while selected and len(json.dumps(selected, ensure_ascii=False)) > budget:
        selected.pop(0)
    return json.dumps(selected, ensure_ascii=False)


def scoped_execution_facts(state: dict) -> list[dict]:
    npc = str((state.get("npc") or {}).get("name", "")).casefold()
    keys = ("npc", "job_id", "expedition_id", "meeting_id", "turn_id", "action", "state",
            "game_day", "time_of_day", "location", "topic", "completed_targets",
            "skipped_targets", "monsters_defeated", "interaction_observed", "following",
            "estimated_travel_minutes", "started_time", "message")
    facts = state.get("execution_facts")
    if not npc or not isinstance(facts, list):
        return []
    return [{k: f[k] for k in keys if k in f} for f in facts
            if isinstance(f, dict) and str(f.get("npc", "")).casefold() == npc][-20:]


def explicit_day(text: str):
    text = text.lower()
    if "后天" in text or "day after tomorrow" in text: return 2
    if "明天" in text or "明晚" in text or "tomorrow" in text: return 1
    if any(word in text for word in ("今天", "今晚", "today", "tonight")): return 0
    return None


def meeting_day(player_input: str, history: list[dict], fallback: int = 0) -> int:
    day = explicit_day(player_input)
    if day is not None: return day
    for message in reversed(history[-6:]):
        if message.get("role") == "user":
            text = str(message.get("content", ""))
            day = explicit_day(text)
            if day is not None and any(word in text.lower() for word in ("有空", "见", "一起", "约", "meet", "free", "together")):
                return day
    return fallback


def farm_location_conflict(player_input: str, state: dict) -> bool:
    text = player_input.lower()
    if any(t in text for t in ("农场", "farm")): return False
    location = ((state.get("npc_perception") or {}).get("location") or {}).get("name", "")
    return bool(location and location != "Farm" and
                any(t in text for t in ("附近", "这里", "山上", "山里", "森林", "nearby", "here", "mountain", "forest")))


def constrain_meeting(generation: dict, state: dict, player_input: str, history: list[dict]) -> dict:
    proposal = generation.get("action_proposal") or {}
    if proposal.get("action") != "schedule_meeting":
        return generation
    parameters = proposal["parameters"]
    agreed_day = None
    time_reference = player_input
    if history and not requested_action(player_input) and re.fullmatch(r"(?:好啊?|好的|可以|行啊?|嗯|那就这样|就这么定了|没问题|yes|sure|okay|ok)[啊呀吧呢，,。.!！\s]*(?:好啊?)*", player_input.strip(), re.I):
        latest = history[-1]
        text = str(latest.get("content", ""))
        if latest.get("role") == "assistant":
            agreed_day = explicit_day(text)
        clocks = list(re.finditer(r"\d{1,2}[:：]\d{2}|[\d一二三四五六七八九十两]+点(?:半|\d{1,2}分?)?", text))
        if latest.get("role") == "assistant" and clocks:
            # A counteroffer can mention the old time first: '11 is busy; meet at 11:30'.
            suggestions = [m for m in clocks if re.search(r"见|碰头|碰面|怎么样|好吗|可以吗|[？?]", text[m.end():m.end() + 40])]
            if suggestions:
                clock = suggestions[-1]
                periods = list(re.finditer(r"早上|上午|下午|傍晚|晚上|今晚|明晚|中午|\bam\b|\bpm\b", text[:clock.start()], re.I))
                time_reference = (periods[-1].group(0) if periods else "") + clock.group(0)
                parameters["time_of_day"] = _parse_meeting_time(None, time_reference)
    day = agreed_day if agreed_day is not None else meeting_day(player_input, history, parameters.get("day_offset", 0))
    parameters["day_offset"] = day
    time = parameters.get("time_of_day", -1)
    rules = state.get("action_rules") or {}
    now = rules.get("current_time") or (((state.get("npc_perception") or {}).get("time") or {}).get("timeOfDay"))
    lead = max(10, int(rules.get("meeting_minimum_lead_minutes", 30)))
    # Resolve bare clock hours locally; explicit morning and HH:MM remain authoritative.
    explicit_period = any(w in time_reference.lower() for w in ("早", "上午", "下午", "傍晚", "晚上", "今晚", "明晚", "中午", "am", "pm"))
    if isinstance(time, int) and 0 < time // 100 < 12 and "点" in time_reference and not explicit_period and not re.search(r"\d[:：]\d", time_reference):
        candidates = [t for t in (time, time + 1200) if 800 <= t <= 2200 and t % 100 < 60 and
                      (day > 0 or now is None or t // 100 * 60 + t % 100 - (int(now) // 100 * 60 + int(now) % 100) >= lead)]
        if candidates:
            time = parameters["time_of_day"] = candidates[0]
    valid = isinstance(time, int) and 800 <= time <= 2200 and time % 100 < 60 and time % 10 == 0
    too_close = now is not None and day == 0 and time // 100 * 60 + time % 100 - (int(now) // 100 * 60 + int(now) % 100) < lead
    if proposal.get("disposition") == "refuse":
        if re.search(r"行啊|好啊|可以|到时见|点见|说定|不见不散|碰头|我会.{0,6}过去", generation["reply"]) and not re.search(r"不行|不可以|没空|不能|再商量|再确认", generation["reply"]):
            generation["reply"] = "这次我还不能答应，先别按这个时间等我，我们再商量一下吧。" if re.search(r"[\u4e00-\u9fff]", player_input) else "I haven't agreed to this meeting; let's discuss it first."
        return generation
    if not valid or too_close:
        proposal["disposition"] = "negotiate"
        generation["reply"] = f"这个时间有点赶，咱们换个时间吧，至少留 {lead} 分钟准备，可以吗？" if re.search(r"[\u4e00-\u9fff]", player_input) else f"Let's pick another time, with at least {lead} minutes to get ready."
        return generation
    if proposal.get("disposition") == "negotiate":
        if re.search(r"行啊|好啊|说定|到时见|不见不散|点.{0,12}见", generation["reply"]) and not re.search(r"[？?]|不行|不能|没空|怎么样|再商量", generation["reply"]):
            generation["reply"] = "我还没最终答应这个安排，我们再商量一下，好吗？" if re.search(r"[\u4e00-\u9fff]", player_input) else "Let's discuss it a little more before we agree."
        return generation
    # Repair only a contradictory appointment token, preserving the character's surrounding wording.
    reply = generation["reply"]
    clock = _requested_clock(reply)
    if clock:
        spoken = _parse_meeting_time(None, reply)
        same_hour = spoken == time or spoken < 1200 and spoken + 1200 == time and not re.search(r"早上|上午|\bam\b", reply, re.I)
        if not same_hour:
            reply = reply[:clock.start()] + f"{time // 100}:{time % 100:02d}" + reply[clock.end():]
    spoken_day = explicit_day(reply)
    if spoken_day is not None and spoken_day != day:
        day_label = ("今天", "明天", "后天")[day] if day <= 2 else f"{day}天后"
        reply = re.sub(r"后天|明天|今天|今晚|明晚", day_label, reply, count=1)
    generation["reply"] = reply
    # The confirmation menu owns the exact schedule; never append a second canned confirmation sentence.
    return generation


def unsupported_delivery(reply: str) -> bool:
    return bool(re.search(r"(?:我.{0,8}(?:发|给|分享).{0,8}链接|(?:去|直接).{0,4}下单|(?:I'll|I will).{0,20}send.{0,15}(?:link|url)|(?:演出|表演).{0,6}(?:完|结束).{0,24}(?:找你|汇报))", reply, re.I))


def contradicts_completed_work(player_input: str, reply: str, state: dict) -> bool:
    if not re.search(r"是不是你|是你.{0,12}(?:吗|的)|did you|have you", player_input, re.I): return False
    action = next((a for a, terms in {"water_crops": ("浇", "water"), "chop_trees": ("砍", "chop"),
                  "clear_weeds": ("草", "weed")}.items() if any(t in player_input.lower() for t in terms)), "")
    completed = any(f.get("action") == action and f.get("state") == "completed" and
                    int(f.get("completed_targets", 0)) > 0 for f in scoped_execution_facts(state))
    # Require a correction, not an automatic success claim: the model must resolve the requested day.
    return completed and bool(re.search(r"不是我|没(?:有)?(?:帮你|浇|砍)|一直在|wasn't me|did not|didn't", reply, re.I))
