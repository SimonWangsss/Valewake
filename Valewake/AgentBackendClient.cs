using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Valewake;

public sealed class AgentBackendClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly Uri memoryCommitEndpoint;
    private readonly Uri memoryRollbackEndpoint;
    private readonly Uri traceEventEndpoint;
    private readonly Uri traceMarkEndpoint;

    public AgentBackendClient(string backendUrl, int timeoutSeconds)
    {
        endpoint = new Uri(backendUrl);
        Uri serviceRoot = new(endpoint, "/");
        memoryCommitEndpoint = new Uri(serviceRoot, "memory/commit");
        memoryRollbackEndpoint = new Uri(serviceRoot, "memory/rollback");
        traceEventEndpoint = new Uri(serviceRoot, "trace/event");
        traceMarkEndpoint = new Uri(serviceRoot, "trace/mark");
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

    public Task CommitMemoryAsync(string sessionPrefix, CancellationToken cancellationToken = default) =>
        SendMemoryOperationAsync(memoryCommitEndpoint, sessionPrefix, cancellationToken);

    public Task RollbackMemoryAsync(string sessionPrefix, CancellationToken cancellationToken = default) =>
        SendMemoryOperationAsync(memoryRollbackEndpoint, sessionPrefix, cancellationToken);

    public Task SendTraceEventAsync(
        string eventName,
        object payload,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(traceEventEndpoint, new { @event = eventName, payload }, cancellationToken);

    public Task MarkTurnAsync(
        string turnId,
        string label,
        string note,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            traceMarkEndpoint,
            new { turn_id = turnId, label, note, tags = Array.Empty<string>() },
            cancellationToken
        );

    private async Task SendMemoryOperationAsync(
        Uri operationEndpoint,
        string sessionPrefix,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(
            new { session_prefix = sessionPrefix },
            JsonOptions
        );
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.PostAsync(
            operationEndpoint,
            content,
            cancellationToken
        );
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Backend memory operation returned {(int)response.StatusCode}: {responseText}"
            );
        }
    }

    private async Task SendJsonAsync(
        Uri target,
        object payload,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.PostAsync(target, content, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode ||
            responseText.Contains("\"ok\":false", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Backend trace operation returned {(int)response.StatusCode}: {responseText}"
            );
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
