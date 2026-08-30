"""Provider registry: official base URLs, API format, and default models.

The game's in-game config menu reads this list so a user only has to pick a
provider and paste an API key; the official URL and model choices are filled in
automatically. ``format`` is either ``openai`` (the de-facto standard
``/chat/completions`` endpoint) or ``anthropic`` (Anthropic's native
``/v1/messages`` endpoint).
"""

PROVIDERS: list[dict] = [
    {
        "id": "deepseek",
        "name": "DeepSeek",
        "format": "openai",
        "base_url": "https://api.deepseek.com",
        "models": ["deepseek-chat", "deepseek-reasoner"],
    },
    {
        "id": "openai",
        "name": "OpenAI",
        "format": "openai",
        "base_url": "https://api.openai.com/v1",
        "models": ["gpt-4o", "gpt-4o-mini", "o3-mini"],
    },
    {
        "id": "anthropic",
        "name": "Anthropic Claude",
        "format": "anthropic",
        "base_url": "https://api.anthropic.com",
        "models": ["claude-3-5-sonnet-latest", "claude-3-5-haiku-latest"],
    },
    {
        "id": "qwen",
        "name": "通义千问 Qwen",
        "format": "openai",
        "base_url": "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "models": ["qwen-plus", "qwen-max", "qwen-turbo"],
    },
    {
        "id": "gemini",
        "name": "Google Gemini",
        "format": "openai",
        "base_url": "https://generativelanguage.googleapis.com/v1beta/openai",
        "models": ["gemini-2.0-flash", "gemini-2.0-pro"],
    },
    {
        "id": "openrouter",
        "name": "OpenRouter",
        "format": "openai",
        "base_url": "https://openrouter.ai/api/v1",
        "models": [
            "deepseek/deepseek-chat",
            "anthropic/claude-3.5-sonnet",
            "openai/gpt-4o",
        ],
    },
    {
        "id": "custom",
        "name": "自定义 / 本地 (Ollama 等)",
        "format": "openai",
        "base_url": "",
        "models": [],
    },
]


def find_provider(provider_id: str) -> dict | None:
    for provider in PROVIDERS:
        if provider["id"] == provider_id:
            return provider
    return None


def provider_by_url(api_base: str) -> dict | None:
    normalized = (api_base or "").rstrip("/").lower()
    for provider in PROVIDERS:
        if provider["base_url"] and normalized == provider["base_url"].rstrip("/").lower():
            return provider
    return None
