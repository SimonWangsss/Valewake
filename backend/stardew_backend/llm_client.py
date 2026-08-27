import json
import time
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
    thinking_mode: str = "disabled"
    max_tokens: int = 700
    json_mode: bool = True


class LLMClient:
    def __init__(self, config: LLMConfig):
        self.config = config
        self.last_metrics: dict[str, object] = {}

    def chat(self, messages: List[Dict[str, str]], temperature: float = 0.45) -> str:
        if self.config.backend == "mock":
            started = time.perf_counter()
            result = self._mock_reply(messages)
            self.last_metrics = {
                "provider": "mock", "latency_ms": round((time.perf_counter() - started) * 1000, 1)
            }
            return result
        if self.config.backend == "openai":
            return self._openai_chat(messages, temperature)
        raise ValueError(f"Unsupported LLM_BACKEND: {self.config.backend}")

    def _openai_chat(self, messages: List[Dict[str, str]], temperature: float) -> str:
        payload = {
            "model": self.config.model,
            "messages": messages,
            "temperature": temperature,
            "max_tokens": self.config.max_tokens,
        }
        is_deepseek = "api.deepseek.com" in self.config.api_base.lower()
        if self.config.json_mode:
            payload["response_format"] = {"type": "json_object"}
        if is_deepseek and self.config.thinking_mode in {"enabled", "disabled", "auto"}:
            payload["thinking"] = {"type": self.config.thinking_mode}
        headers = {"Content-Type": "application/json"}
        if self.config.api_key:
            headers["Authorization"] = f"Bearer {self.config.api_key}"

        request = urllib.request.Request(
            f"{self.config.api_base}/chat/completions",
            data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        started = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=self.config.timeout_seconds) as response:
                data = json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"LLM HTTP {exc.code}: {body}") from exc

        usage = data.get("usage") or {}
        message = data["choices"][0]["message"]
        self.last_metrics = {
            "provider": "deepseek" if is_deepseek else "openai_compatible",
            "model": self.config.model,
            "thinking_mode": self.config.thinking_mode if is_deepseek else "provider_default",
            "latency_ms": round((time.perf_counter() - started) * 1000, 1),
            "prompt_tokens": usage.get("prompt_tokens"),
            "completion_tokens": usage.get("completion_tokens"),
            "total_tokens": usage.get("total_tokens"),
        }
        reasoning = message.get("reasoning_content")
        if reasoning:
            self.last_metrics["reasoning_chars"] = len(reasoning)
        return str(message.get("content") or "").strip()

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
