using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StardewAgentFramework;

public sealed class AgentBackendClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;

    public AgentBackendClient(string backendUrl, int timeoutSeconds)
    {
        endpoint = new Uri(backendUrl);
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))
        };
    }

    public async Task<AgentChatResponse> SendChatAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(request, JsonOptions);
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Backend returned {(int)response.StatusCode}: {responseText}");
        }

        AgentChatResponse? parsed = JsonSerializer.Deserialize<AgentChatResponse>(responseText, JsonOptions);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Reply))
        {
            throw new InvalidOperationException("Backend response did not contain a reply.");
        }

        return parsed;
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
