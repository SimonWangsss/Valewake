using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;

namespace StardewAgentFramework;

public sealed class RelationshipManager
{
    private const string SaveDataKey = "agent-relationship-state-v1";
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private RelationshipSaveData data = new();

    public RelationshipManager(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
    }

    public void Load()
    {
        data = helper.Data.ReadSaveData<RelationshipSaveData>(SaveDataKey) ?? new RelationshipSaveData();
    }

    public AgentRelationshipContext GetContext(string npcName)
    {
        NpcRelationshipState state = GetState(npcName);
        return new AgentRelationshipContext
        {
            Rapport = state.Rapport,
            Trust = state.Trust,
            RecentMood = state.RecentMood,
            LastReason = state.LastReason
        };
    }

    public RelationshipApplicationResult Apply(NPC npc, RelationshipEffect effect)
    {
        if (!config.EnableAiFriendshipChanges)
            return RelationshipApplicationResult.Rejected("AI friendship changes are disabled.");

        string valence = (effect.Valence ?? "neutral").Trim().ToLowerInvariant();
        if (valence == "neutral" || effect.Intensity <= 0)
            return RelationshipApplicationResult.Rejected("Neutral conversation.");
        if (valence != "positive" && valence != "negative")
            return RelationshipApplicationResult.Rejected("Unknown relationship valence.");
        if (effect.Confidence < config.MinimumRelationshipConfidence)
            return RelationshipApplicationResult.Rejected("Relationship confidence was below the policy threshold.");
        if (string.IsNullOrWhiteSpace(effect.Reason) || string.IsNullOrWhiteSpace(effect.Evidence))
            return RelationshipApplicationResult.Rejected("Relationship assessment had no reason or evidence.");

        int intensity = Math.Clamp(effect.Intensity, 1, 2);
        int direction = valence == "positive" ? 1 : -1;
        int requestedPoints = direction * intensity * Math.Max(1, config.FriendshipPointsPerIntensity);
        NpcRelationshipState state = GetState(npc.Name);
        ResetDailyLedgerIfNeeded(state);

        int remaining = direction > 0
            ? Math.Max(0, config.MaxAiFriendshipGainPerDay - state.GainedToday)
            : Math.Max(0, config.MaxAiFriendshipLossPerDay - state.LostToday);
        int appliedMagnitude = Math.Min(Math.Abs(requestedPoints), remaining);
        if (appliedMagnitude == 0)
            return RelationshipApplicationResult.Rejected("The daily AI friendship limit has been reached.");

        int appliedPoints = direction * appliedMagnitude;
        Game1.player.changeFriendship(appliedPoints, npc);
        if (direction > 0)
            state.GainedToday += appliedMagnitude;
        else
            state.LostToday += appliedMagnitude;

        state.Rapport = Math.Clamp(state.Rapport + direction * intensity * 2, -100, 100);
        if (intensity == 2)
            state.Trust = Math.Clamp(state.Trust + direction, 0, 100);
        state.RecentMood = valence;
        state.LastReason = effect.Reason;
        state.LastUpdatedDay = Game1.Date.TotalDays;
        helper.Data.WriteSaveData(SaveDataKey, data);

        monitor.Log(
            $"Relationship policy applied {appliedPoints:+#;-#;0} friendship to {npc.Name}. " +
            $"Rapport={state.Rapport}, Trust={state.Trust}, Reason={effect.Reason}",
            LogLevel.Info
        );
        return RelationshipApplicationResult.Applied(appliedPoints, BuildFeedback(npc, direction, appliedPoints));
    }

    private string BuildFeedback(NPC npc, int direction, int points)
    {
        string message = direction > 0
            ? $"{npc.displayName} enjoyed that conversation."
            : $"{npc.displayName} was bothered by that conversation.";
        if (config.ShowNumericFriendshipChange)
            message += $" ({points:+#;-#;0} friendship)";
        return message;
    }

    private NpcRelationshipState GetState(string npcName)
    {
        if (!data.Npcs.TryGetValue(npcName, out NpcRelationshipState? state))
        {
            state = new NpcRelationshipState();
            data.Npcs[npcName] = state;
        }
        return state;
    }

    private static void ResetDailyLedgerIfNeeded(NpcRelationshipState state)
    {
        if (state.LedgerDay == Game1.Date.TotalDays)
            return;
        state.LedgerDay = Game1.Date.TotalDays;
        state.GainedToday = 0;
        state.LostToday = 0;
    }
}

public sealed class RelationshipSaveData
{
    public Dictionary<string, NpcRelationshipState> Npcs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class NpcRelationshipState
{
    public int Rapport { get; set; }
    public int Trust { get; set; }
    public string RecentMood { get; set; } = "neutral";
    public string LastReason { get; set; } = "";
    public int LastUpdatedDay { get; set; } = -1;
    public int LedgerDay { get; set; } = -1;
    public int GainedToday { get; set; }
    public int LostToday { get; set; }
}

public sealed class AgentRelationshipContext
{
    public int Rapport { get; init; }
    public int Trust { get; init; }
    public string RecentMood { get; init; } = "neutral";
    public string LastReason { get; init; } = "";
}

public sealed class RelationshipApplicationResult
{
    public bool WasApplied { get; init; }
    public int FriendshipPoints { get; init; }
    public string Feedback { get; init; } = "";
    public string Reason { get; init; } = "";

    public static RelationshipApplicationResult Applied(int points, string feedback) =>
        new() { WasApplied = true, FriendshipPoints = points, Feedback = feedback };

    public static RelationshipApplicationResult Rejected(string reason) =>
        new() { WasApplied = false, Reason = reason };
}
