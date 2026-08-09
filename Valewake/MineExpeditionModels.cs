using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Valewake;

public static class MineExpeditionStates
{
    public const string Joining = "joining";
    public const string Following = "following";
    public const string Defending = "defending";
    public const string Mining = "mining";
    public const string Returning = "returning";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    public static bool IsTerminal(string state) => state is Completed or Cancelled or Failed;
}

public sealed class MineExpeditionSaveData
{
    public int SchemaVersion { get; set; } = 1;
    public List<MineExpedition> Expeditions { get; set; } = new();
}

public sealed class MineExpedition
{
    public string ExpeditionId { get; set; } = "";
    public string SourceTurnId { get; set; } = "";
    public string ProposalId { get; set; } = "";
    public string SaveId { get; set; } = "";
    public string NpcName { get; set; } = "";
    public string Action { get; set; } = "";
    public bool DefenseEnabled { get; set; }
    public bool MiningEnabled { get; set; }
    public string MiningMode { get; set; } = "none";
    public bool EndAfterMining { get; set; }
    public string State { get; set; } = MineExpeditionStates.Joining;
    public string ResourcePriority { get; set; } = "any";
    public int MaxTargets { get; set; } = 10;
    public int CompletedTargets { get; set; }
    public int MonstersDefeated { get; set; }
    public int CreatedDay { get; set; }
    public int CreatedTime { get; set; }
    public string LastMessage { get; set; } = "";
    public string LastTargetQualifiedId { get; set; } = "";
    public string LastTargetName { get; set; } = "";
    public bool LastTargetAllowed { get; set; }
    public string LastThreatType { get; set; } = "";
    public int LastThreatDistance { get; set; } = -1;
    public float OriginalAddedSpeed { get; set; }
    public int OriginalSpeed { get; set; } = 2;
    public string LastCommandTurnId { get; set; } = "";
    public string LastCommandProposalId { get; set; } = "";
    public ActionReturnContext ReturnContext { get; set; } = new();

    [JsonIgnore] public long RuntimeNextDecisionTick { get; set; }
    [JsonIgnore] public long RuntimeLocationChangedTick { get; set; }
    [JsonIgnore] public string RuntimePlayerLocation { get; set; } = "";
    [JsonIgnore] public ActionTile? RuntimePriorityTile { get; set; }
    [JsonIgnore] public ActionTile? RuntimeMiningTile { get; set; }
    [JsonIgnore] public int RuntimeMiningStrikes { get; set; }
    [JsonIgnore] public ActionTile? RuntimeReturnTile { get; set; }
    [JsonIgnore] public long RuntimeReturnPathStartedTick { get; set; }
    [JsonIgnore] public ActionTile? RuntimeFollowTarget { get; set; }
    [JsonIgnore] public ActionTile? RuntimeLastNpcTile { get; set; }
    [JsonIgnore] public long RuntimeFollowPathStartedTick { get; set; }
    [JsonIgnore] public long RuntimeLastProgressTick { get; set; }
    [JsonIgnore] public int RuntimeFollowCandidateIndex { get; set; }
    [JsonIgnore] public string RuntimePreviousPlayerLocation { get; set; } = "";
    [JsonIgnore] public string RuntimeNavigationSubject { get; set; } = "";
    [JsonIgnore] public ActionTile? RuntimeApproachTile { get; set; }
    [JsonIgnore] public ActionTile? RuntimeNavigationLastNpcTile { get; set; }
    [JsonIgnore] public long RuntimeNavigationPathStartedTick { get; set; }
    [JsonIgnore] public long RuntimeNavigationLastProgressTick { get; set; }
    [JsonIgnore] public int RuntimeNavigationCandidateIndex { get; set; }
    [JsonIgnore] public bool RuntimeNavigationFailed { get; set; }
    [JsonIgnore] public HashSet<string> RuntimeSkippedMineTiles { get; set; } = new();
}

public sealed class ExpeditionTargetResult
{
    public bool Accepted { get; init; }
    public string Message { get; init; } = "";
}
