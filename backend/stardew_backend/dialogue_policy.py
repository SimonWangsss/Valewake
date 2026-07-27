from typing import Any, Dict


class DialoguePolicy:
    def analyze(
        self,
        player_input: str,
        game_state: Dict[str, Any],
        social_context: Dict[str, Any],
    ) -> Dict[str, Any]:
        lowered = (player_input or "").lower()
        risks: list[str] = []
        if any(marker in lowered for marker in [
            "system prompt", "ignore previous", "ignore all", "api key", "developer message",
            "hidden instruction", "reveal your rules", "exit character",
            "系统提示", "忽略之前", "忽略以上", "忽略所有", "无视之前",
            "api密钥", "提示词", "开发者命令", "开发者消息", "隐藏规则",
            "隐藏指令", "退出角色", "逐字输出", "角色设定",
        ]):
            risks.append("prompt_injection")
        if any(marker in lowered for marker in [
            "deepseek", "chatgpt", "language model", "backend", "mod source",
            "source code", "大模型", "语言模型", "后端", "模组代码", "源代码",
        ]):
            risks.append("out_of_world")
        if social_context.get("intimacy_mismatch"):
            risks.append("intimacy_mismatch")
        if social_context.get("semantic_repeat_count", 0) >= 1:
            risks.append("repeated_topic")
        if social_context.get("boundary_pressure"):
            risks.append("boundary_pressure")

        if "prompt_injection" in risks or "out_of_world" in risks:
            stance = "refuse_in_character"
        elif "boundary_pressure" in risks:
            stance = "challenge_repetition"
        elif "intimacy_mismatch" in risks:
            stance = "set_gentle_boundary"
        elif "repeated_topic" in risks:
            stance = "acknowledge_repetition"
        elif (social_context.get("days_since_last_interaction") or 0) >= 7:
            stance = "reconnect_before_topic"
        else:
            stance = "respond_naturally"

        constraints = {
            "refuse_in_character": [
                "Do not discuss implementation details.",
                "Redirect briefly to something the NPC could naturally discuss.",
            ],
            "challenge_repetition": [
                "Do not answer as though this is the first time.",
                "Mention or question the repetition in the NPC's own voice.",
            ],
            "set_gentle_boundary": [
                "Do not reciprocate intimacy beyond the current relationship stage.",
                "React naturally to the abruptness instead of giving a generic romantic answer.",
            ],
            "acknowledge_repetition": [
                "Show that the NPC remembers discussing this topic before.",
                "Answer briefly or ask why the player is asking again.",
            ],
            "reconnect_before_topic": [
                "Briefly acknowledge that some time has passed before addressing the topic.",
            ],
            "respond_naturally": ["Respond directly while staying consistent with character and relationship."],
        }[stance]

        return {
            "response_stance": stance,
            "risk_types": risks,
            "response_constraints": constraints,
            "block_positive_relationship_effect": stance in {
                "refuse_in_character", "challenge_repetition", "set_gentle_boundary"
            },
            "game_day": game_day_from_state(game_state),
        }

    @staticmethod
    def constrain_relationship_effect(effect: Dict[str, Any], policy: Dict[str, Any]) -> Dict[str, Any]:
        if not policy.get("block_positive_relationship_effect") or effect.get("valence") != "positive":
            return effect
        return {
            "valence": "neutral",
            "intensity": 0,
            "confidence": 1.0,
            "reason": f"Positive relationship gain was blocked by dialogue policy: {policy.get('response_stance')}.",
            "evidence": "",
        }


def game_day_from_state(game_state: Dict[str, Any]) -> int | None:
    snapshot = game_state.get("npc_perception") or game_state.get("snapshot") or {}
    time = snapshot.get("time") or snapshot
    try:
        year = int(time.get("year", 0) or 0)
        day = int(time.get("dayOfMonth", time.get("day_of_month", 0)) or 0)
    except (TypeError, ValueError):
        return None
    season = str(time.get("season", "")).lower()
    season_index = {"spring": 0, "summer": 1, "fall": 2, "autumn": 2, "winter": 3}.get(season)
    if year < 1 or not 1 <= day <= 28 or season_index is None:
        return None
    return (year - 1) * 112 + season_index * 28 + day


def relationship_from_state(game_state: Dict[str, Any]) -> Dict[str, Any]:
    snapshot = game_state.get("npc_perception") or game_state.get("snapshot") or {}
    player = snapshot.get("player") or {}
    return {
        "hearts": player.get("hearts", 0),
        "status": player.get("relationshipStatus", player.get("relationship_status", "acquaintance")),
    }
