using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Valewake;

public static class ActionIds
{
    public const string WaterCrops = "water_crops";
    public const string ClearWeeds = "clear_weeds";
}

public static class ActionJobStates
{
    public const string Accepted = "accepted";
    public const string Dispatching = "dispatching";
    public const string Preparing = "preparing";
    public const string Navigating = "navigating";
    public const string Acting = "acting";
    public const string Returning = "returning";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string FailedRecoverable = "failed_recoverable";
    public const string FailedTerminal = "failed_terminal";

    public static bool IsTerminal(string state) =>
        state is Completed or Cancelled or FailedRecoverable or FailedTerminal;
}

public sealed class ActionJobSaveData
{
    public int SchemaVersion { get; set; } = 1;
    public List<ActionJob> Jobs { get; set; } = new();
}

public sealed class ActionJob
{
    public string JobId { get; set; } = "";
    public string ProposalId { get; set; } = "";
    public string SaveId { get; set; } = "";
    public string NpcName { get; set; } = "";
    public string Action { get; set; } = "";
    public string State { get; set; } = ActionJobStates.Accepted;
    public int MaxTargets { get; set; } = 10;
    public int CreatedDay { get; set; }
    public int CreatedTime { get; set; }
    public int AnchorX { get; set; } = -1;
    public int AnchorY { get; set; } = -1;
    public List<ActionTile> Targets { get; set; } = new();
    public int CurrentTargetIndex { get; set; }
    public int CompletedTargets { get; set; }
    public int FailedTargets { get; set; }
    public string LastMessage { get; set; } = "";
    public ActionReturnContext ReturnContext { get; set; } = new();

    [JsonIgnore]
    public long RuntimeNextTick { get; set; }

    [JsonIgnore]
    public long RuntimePathStartedTick { get; set; }

    [JsonIgnore]
    public long RuntimeDispatchDeadlineTick { get; set; }

    [JsonIgnore]
    public long RuntimeFarmArrivalTick { get; set; }

    [JsonIgnore]
    public ActionTile? RuntimeStandTile { get; set; }

    [JsonIgnore]
    public List<ActionTile> RuntimeStandCandidates { get; set; } = new();

    [JsonIgnore]
    public int RuntimeStandCandidateIndex { get; set; }

    [JsonIgnore]
    public ActionTile? RuntimeReturnTile { get; set; }
}

public sealed class ActionTile
{
    public int X { get; set; }
    public int Y { get; set; }

    public ActionTile()
    {
    }

    public ActionTile(int x, int y)
    {
        X = x;
        Y = y;
    }
}

public sealed class ActionReturnContext
{
    public string LocationName { get; set; } = "";
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int FacingDirection { get; set; } = 2;
    public bool FollowSchedule { get; set; } = true;
}

public sealed class ActionProposalDecision
{
    public bool Allowed { get; init; }
    public string Message { get; init; } = "";
    public string ConfirmationQuestion { get; init; } = "";
    public int MaxTargets { get; init; }

    public static ActionProposalDecision Reject(string message) =>
        new() { Message = message };

    public static ActionProposalDecision Allow(string question, int maxTargets) =>
        new() { Allowed = true, ConfirmationQuestion = question, MaxTargets = maxTargets };
}
