import json
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Dict, List


@dataclass(frozen=True)
class LLMConfig:
    backend: str
    api_base: str
    api_key: str
    model: str
    timeout_seconds: int


class LLMClient:
    def __init__(self, config: LLMConfig):
        self.config = config

    def chat(self, messages: List[Dict[str, str]], temperature: float = 0.45) -> str:
        if self.config.backend == "mock":
            return self._mock_reply(messages)
        if self.config.backend == "openai":
            return self._openai_chat(messages, temperature)
        raise ValueError(f"Unsupported LLM_BACKEND: {self.config.backend}")

    def _openai_chat(self, messages: List[Dict[str, str]], temperature: float) -> str:
        payload = {
            "model": self.config.model,
            "messages": messages,
            "temperature": temperature,
            "max_tokens": 520,
        }
        headers = {"Content-Type": "application/json"}
        if self.config.api_key:
            headers["Authorization"] = f"Bearer {self.config.api_key}"

        request = urllib.request.Request(
            f"{self.config.api_base}/chat/completions",
            data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.config.timeout_seconds) as response:
                data = json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"LLM HTTP {exc.code}: {body}") from exc

        return data["choices"][0]["message"]["content"].strip()

    def _mock_reply(self, messages: List[Dict[str, str]]) -> str:
        user_text = messages[-1]["content"] if messages else ""
        player_text = user_text.rsplit("Player says:\n", 1)[-1].split("\n\n", 1)[0]
        lowered = player_text.lower()
        positive = any(word in lowered for word in ["thank", "like you", "adventure", "一起", "喜欢", "谢谢"])
        negative = any(word in lowered for word in ["hate you", "stupid", "shut up", "讨厌你", "闭嘴", "笨蛋"])
        if negative:
            return json.dumps({
                "reply": "Okay... that was uncalled for.",
                "emotion": "angry",
                "relationship_effect": {
                    "valence": "negative", "intensity": 1, "confidence": 0.9,
                    "reason": "The player spoke disrespectfully.", "evidence": player_text[:160]
                },
                "memory_candidates": [], "action_proposal": None
            })
        if positive:
            return json.dumps({
                "reply": "You really mean that? Heh, maybe we should go exploring together sometime.",
                "emotion": "happy",
                "relationship_effect": {
                    "valence": "positive", "intensity": 1, "confidence": 0.86,
                    "reason": "The player showed sincere interest in Abigail's adventurous side.",
                    "evidence": player_text[:160]
                },
                "memory_candidates": [], "action_proposal": None
            })
        if '"isRaining": true' in user_text:
            reply = "Looks like rain's doing the watering today. Maybe it's a good day to explore somewhere strange."
        elif "prompt" in lowered or "system" in lowered:
            reply = "Nice try. Let's keep this about the valley, okay?"
        else:
            reply = "I'm listening. What's on your mind?"
        return json.dumps({
            "reply": reply,
            "emotion": "neutral",
            "relationship_effect": {
                "valence": "neutral", "intensity": 0, "confidence": 0.8,
                "reason": "No clear relationship-changing moment occurred.", "evidence": ""
            },
            "memory_candidates": [], "action_proposal": None
        })
