using System;

namespace Valewake;

public sealed class ModConfig
{
    public bool EnableMod { get; set; } = true;
    public bool DebugLogging { get; set; } = true;
    public string BackendUrl { get; set; } = "http://127.0.0.1:8010/chat";
    public string BackendHealthUrl { get; set; } = "http://127.0.0.1:8010/health";
    public bool AutoStartBackend { get; set; } = true;
    public string BackendExecutablePath { get; set; } = @"Backend\ValewakeBackend.exe";
    public string BackendWorkingDirectory { get; set; } = "Backend";
    public int BackendStartupTimeoutSeconds { get; set; } = 20;
    public string AgentName { get; set; } = "Valewake";
    public bool EnableAllSocialNpcs { get; set; } = true;
    public string[] ExcludedNpcNames { get; set; } = Array.Empty<string>();
    // Retained as a compatibility fallback when EnableAllSocialNpcs is false.
    public string TargetNpcName { get; set; } = "Abigail";
    public string StateCommandName { get; set; } = "agent_state";
    public string ChatCommandName { get; set; } = "agent_chat";
    public int NpcInteractionRadiusTiles { get; set; } = 2;
    public int BackendTimeoutSeconds { get; set; } = 30;
    public bool EnableRightClickChat { get; set; } = true;
    public bool PreserveVanillaFirstDialogue { get; set; } = true;
    public int MaxConversationHistoryMessages { get; set; } = 12;
    public bool EnableAiFriendshipChanges { get; set; } = true;
    public int FriendshipPointsPerIntensity { get; set; } = 5;
    public int MaxAiFriendshipGainPerDay { get; set; } = 10;
    public int MaxAiFriendshipLossPerDay { get; set; } = 10;
    public double MinimumRelationshipConfidence { get; set; } = 0.72;
    public bool ShowRelationshipFeedback { get; set; } = true;
    public bool ShowNumericFriendshipChange { get; set; } = false;
    public bool EnableActionAgent { get; set; } = true;
    public bool EnableWaterCropsAction { get; set; } = true;
    public bool EnableClearWeedsAction { get; set; } = true;
    public bool EnableCrossMapDispatch { get; set; } = true;
    public int CrossMapDispatchWaitSeconds { get; set; } = 60;
    public int CrossMapFarmLeadSeconds { get; set; } = 8;
    public int MinimumActionHearts { get; set; } = 2;
    public int MinimumActionTrust { get; set; } = 0;
    public double MinimumActionConfidence { get; set; } = 0.65;
    public int MaxWaterTilesPerJob { get; set; } = 10;
    public int MaxWeedsPerJob { get; set; } = 10;
    public int ActionTargetRadiusTiles { get; set; } = 12;
    public int ActionPathTimeoutTicks { get; set; } = 600;
}
