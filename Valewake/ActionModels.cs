using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Valewake;

public static class ActionIds
{
    public const string WaterCrops = "water_crops";
    public const string ClearWeeds = "clear_weeds";
    public const string ChopTrees = "chop_trees";
    public const string JoinMineExpedition = "join_mine_expedition";
    public const string DefendPlayer = "defend_player";
    public const string MineTarget = "mine_target";
    public const string MineNearby = "mine_nearby";
    public const string MineExpedition = "mine_expedition";
    public const string ScheduleMeeting = "schedule_meeting";

    public static bool IsMineAction(string action) => action is
        JoinMineExpedition or DefendPlayer or MineTarget or MineNearby or MineExpedition;
}

/// <summary>
/// Maps a game NPC's internal name to a stable canonical name so that the same
/// person in different game states (for example Morris vs MorrisTod, or Mr Qi vs
/// MrQi) shares memory, persona, and other per-character state instead of being
/// treated as two strangers.
/// </summary>
public static class NpcNameMap
{
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MorrisTod"] = "Morris",
            ["MrQi"] = "Mr Qi",
        };

    public static string Canonical(string name)
    {
        string trimmed = (name ?? "").Trim();
        return Aliases.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
    }
}

public static class ActionJobStates
{
    public const string Accepted = "accepted";
    public const string Dispatching = "dispatching";
    public const string Preparing = "preparing";
    public const string Navigating = "navigating";
    public const string Acting = "acting";
    public const string BackgroundWorking = "background_working";
    public const string Returning = "returning";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string FailedRecoverable = "failed_recoverable";
    public const string FailedTerminal = "failed_terminal";

    public static bool IsTerminal(string state) =>
        state is Completed or Cancelled or FailedRecoverable or FailedTerminal;
}

public static class ActionOriginKinds
{
    public const string Farm = "farm";
    public const string FarmHouse = "farmhouse";
    public const string Boundary = "boundary";
}

public sealed class ActionJobSaveData
{
    public int SchemaVersion { get; set; } = 1;
    public List<ActionJob> Jobs { get; set; } = new();
}

public sealed class ActionJob
{
    public string JobId { get; set; } = "";
    public string SourceTurnId { get; set; } = "";
    public string ProposalId { get; set; } = "";
    public string SaveId { get; set; } = "";
    public string NpcName { get; set; } = "";
    public string Action { get; set; } = "";
    public string State { get; set; } = ActionJobStates.Accepted;
    // Zero means all eligible targets captured when the job begins.
    public int MaxTargets { get; set; }
    public int CreatedDay { get; set; }
    public int CreatedTime { get; set; }
    public long AcceptedTick { get; set; }
    public int BaselineEligibleTargetCount { get; set; }
    public int BaselineSelectedTargetCount { get; set; }
    public int TargetPathAttemptCount { get; set; }
    public int TargetPathRetryCount { get; set; }
    public int ReturnPathAttemptCount { get; set; }
    public int ReturnPathRetryCount { get; set; }
    public int AnchorX { get; set; } = -1;
    public int AnchorY { get; set; } = -1;
    public List<ActionTile> Targets { get; set; } = new();
    public int CurrentTargetIndex { get; set; }
    public int CompletedTargets { get; set; }
    public int FailedTargets { get; set; }
    public string LastMessage { get; set; } = "";
    public string LastTargetType { get; set; } = "";
    public string LastTargetQualifiedId { get; set; } = "";
    public bool LastTargetAllowed { get; set; }
    public ActionTile? LastTarget { get; set; }
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

    [JsonIgnore]
    public ActionTile? RuntimeDispatchTile { get; set; }

    [JsonIgnore]
    public List<ActionTile> RuntimeReturnCandidates { get; set; } = new();

    [JsonIgnore]
    public int RuntimeReturnCandidateIndex { get; set; }

    [JsonIgnore]
    public long RuntimeActionStartedTick { get; set; }

    [JsonIgnore]
    public long RuntimeBackgroundNextTick { get; set; }

    [JsonIgnore]
    public int RuntimeTargetStrikes { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> RuntimeBaselineWorldState { get; set; } = new();

    [JsonIgnore]
    public string RuntimeLastTargetPreState { get; set; } = "";

    [JsonIgnore]
    public string RuntimeLastTargetPostState { get; set; } = "";

    [JsonIgnore]
    public bool RuntimeLastMutationObserved { get; set; }
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
    public string OriginKind { get; set; } = ActionOriginKinds.Boundary;
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
