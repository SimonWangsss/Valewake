using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Valewake;

/// <summary>Root of a scenario file consumed by the SMAPI ScenarioRunner.</summary>
public sealed class ScenarioDefinition
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = "";

    [JsonPropertyName("save_allowlist")]
    public List<string> SaveAllowlist { get; set; } = new();

    [JsonPropertyName("npc")]
    public string Npc { get; set; } = "Emily";

    [JsonPropertyName("days")]
    public List<ScenarioDay> Days { get; set; } = new();
}

/// <summary>One in-game day of scenario steps.</summary>
public sealed class ScenarioDay
{
    [JsonPropertyName("day")]
    public int Day { get; set; } = 1;

    [JsonPropertyName("time")]
    public int Time { get; set; } = 900;

    [JsonPropertyName("location")]
    public string Location { get; set; } = "Farm";

    [JsonPropertyName("chat_input")]
    public string ChatInput { get; set; } = "";

    [JsonPropertyName("wait_terminal_ticks")]
    public int WaitTerminalTicks { get; set; } = 7200;

    [JsonPropertyName("expect_action")]
    public string ExpectAction { get; set; } = "";
}

/// <summary>Per-day outcome recorded into the scenario report.</summary>
public sealed class ScenarioDayResult
{
    public int DayIndex { get; set; }
    public int GameDay { get; set; }
    public int Time { get; set; }
    public string Location { get; set; } = "";
    public string ChatInput { get; set; } = "";
    public string ExpectAction { get; set; } = "";
    public string TurnId { get; set; } = "";
    public string Reply { get; set; } = "";
    public string ProposedAction { get; set; } = "";
    public bool Accepted { get; set; }
    public string JobState { get; set; } = "";
    public int CompletedTargets { get; set; }
    public int FailedTargets { get; set; }
    public int TotalTargets { get; set; }
    public string Error { get; set; } = "";
}
