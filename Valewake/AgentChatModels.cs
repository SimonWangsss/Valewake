using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Valewake;

public sealed class AgentChatRequest
{
    [JsonPropertyName("player_input")]
    public string PlayerInput { get; init; } = "";

    [JsonPropertyName("game_state")]
    public object GameState { get; init; } = new();

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "default";

    [JsonPropertyName("conversation_history")]
    public List<AgentConversationMessage> ConversationHistory { get; init; } = new();

    [JsonPropertyName("debug")]
    public bool Debug { get; init; }
}

public sealed class AgentConversationMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

public sealed class AgentChatResponse
{
    [JsonPropertyName("reply")]
    public string Reply { get; init; } = "";

    [JsonPropertyName("emotion")]
    public string Emotion { get; init; } = "neutral";

    [JsonPropertyName("retrieved_lore")]
    public List<string> RetrievedLore { get; init; } = new();

    [JsonPropertyName("retrieved_memory")]
    public List<string> RetrievedMemory { get; init; } = new();

    [JsonPropertyName("saved_memories")]
    public List<string> SavedMemories { get; init; } = new();

    [JsonPropertyName("relationship_effect")]
    public RelationshipEffect RelationshipEffect { get; init; } = new();

    [JsonPropertyName("action_proposal")]
    public AgentActionProposal? ActionProposal { get; init; }

    [JsonPropertyName("turn_id")]
    public string TurnId { get; init; } = "";

    [JsonPropertyName("debug_prompt")]
    public string? DebugPrompt { get; init; }
}

public sealed class RelationshipEffect
{
    [JsonPropertyName("valence")]
    public string Valence { get; init; } = "neutral";

    [JsonPropertyName("intensity")]
    public int Intensity { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = "";
}

public sealed class AgentActionProposal
{
    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "";

    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    [JsonPropertyName("requires_confirmation")]
    public bool RequiresConfirmation { get; init; }
}
