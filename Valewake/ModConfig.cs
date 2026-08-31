using System;
using StardewModdingAPI;

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

    // LLM provider settings, editable in-game via Generic Mod Config Menu.
    public string LlmProvider { get; set; } = "deepseek";
    public string LlmApiKey { get; set; } = "";
    public string LlmModel { get; set; } = "deepseek-v4-flash";
    public string LlmApiBase { get; set; } = "";

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
    public bool EnableChopTreesAction { get; set; } = false;
    public bool EnableCrossMapDispatch { get; set; } = true;
    public int CrossMapDispatchWaitSeconds { get; set; } = 60;
    public int CrossMapFarmLeadSeconds { get; set; } = 8;
    public int MinimumActionHearts { get; set; } = 2;
    public int MinimumActionTrust { get; set; } = 0;
    public double MinimumActionConfidence { get; set; } = 0.65;
    public int ActionRequestAttemptLimit { get; set; } = 3;
    public int MaxWaterTilesPerJob { get; set; } = 10;
    public int MaxWeedsPerJob { get; set; } = 10;
    public int MaxTreesPerJob { get; set; } = 3;
    public int ActionTargetRadiusTiles { get; set; } = 12;
    public int ActionPathTimeoutTicks { get; set; } = 1800;
    public bool EnableOffscreenFarmWork { get; set; } = true;
    public int OffscreenWorkIntervalSeconds { get; set; } = 2;
    public bool EnableMineExpeditions { get; set; } = true;
    public int MinimumExpeditionHearts { get; set; } = 4;
    public bool ExpeditionAutoMineOnJoin { get; set; } = true;
    public bool ExpeditionAutoDefendOnJoin { get; set; } = true;
    public int ExpeditionFollowDistanceTiles { get; set; } = 2;
    public int ExpeditionFollowSpeedBoost { get; set; } = 2;
    public int ExpeditionRepathIntervalTicks { get; set; } = 45;
    public int ExpeditionStallRecoveryTicks { get; set; } = 120;
    public int ExpeditionOffscreenCatchUpTicks { get; set; } = 240;
    public int ExpeditionWarpDelayTicks { get; set; } = 75;
    public int ExpeditionDefenseRadiusTiles { get; set; } = 6;
    public int ExpeditionMiningRadiusTiles { get; set; } = 8;
    public int ExpeditionMaxMineTargets { get; set; } = 10;
    public int ExpeditionAttackDamage { get; set; } = 12;
    public int ExpeditionEndTime { get; set; } = 2300;
    public int ExpeditionTargetSnapRadiusTiles { get; set; } = 1;
    public SButton ExpeditionTargetButton { get; set; } = SButton.G;

    // Scenario test runner (Track B). Disabled by default; normal play is unaffected while TestMode=false.
    public bool TestMode { get; set; } = false;
    public bool TestAutoRun { get; set; } = false;
    public bool TestAutoConfirm { get; set; } = true;
    public string TestAutoLoadSave { get; set; } = "";
    public string TestScenarioFile { get; set; } = "data/scenarios/seven_day_smoke.json";
    public string[] TestSaveAllowlist { get; set; } = Array.Empty<string>();
    public string[] TestSaveQueue { get; set; } = Array.Empty<string>();
    public int TestWaitTerminalTicks { get; set; } = 7200;
    public bool TestAutoExitAfterRun { get; set; } = false;
}
