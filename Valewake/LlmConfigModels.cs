using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Valewake;

public sealed class LlmProviderOption
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("format")]
    public string Format { get; init; } = "openai";

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; init; } = "";

    [JsonPropertyName("models")]
    public List<string> Models { get; init; } = new();
}

public sealed class LlmCurrentConfig
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "";

    [JsonPropertyName("backend")]
    public string Backend { get; init; } = "openai";

    [JsonPropertyName("api_base")]
    public string ApiBase { get; init; } = "";

    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("has_api_key")]
    public bool HasApiKey { get; init; }
}

public sealed class LlmConfigResponse
{
    [JsonPropertyName("providers")]
    public List<LlmProviderOption> Providers { get; init; } = new();

    [JsonPropertyName("current")]
    public LlmCurrentConfig Current { get; init; } = new();
}

public sealed class LlmConfigRequest
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "";

    [JsonPropertyName("api_base")]
    public string ApiBase { get; init; } = "";

    [JsonPropertyName("api_key")]
    public string ApiKey { get; init; } = "";

    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("backend")]
    public string Backend { get; init; } = "openai";
}
