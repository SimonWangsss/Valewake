using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Valewake;

public static class MeetingStates
{
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Done = "done";
    public const string Missed = "missed";
}

public sealed class MeetingSaveData
{
    public int SchemaVersion { get; set; } = 1;
    public List<ScheduledMeeting> Meetings { get; set; } = new();
}

public sealed class ScheduledMeeting
{
    public string MeetingId { get; set; } = "";
    public string SourceTurnId { get; set; } = "";
    public string NpcName { get; set; } = "";
    public string Location { get; set; } = "";
    public int Day { get; set; }
    public int Time { get; set; }
    public string Topic { get; set; } = "";
    public string State { get; set; } = MeetingStates.Scheduled;
    public string LastMessage { get; set; } = "";
}

/// <summary>
/// Schedules and realizes in-game appointments that an NPC promises during dialogue
/// (for example Alex suggesting "meet me at the beach at 6pm tomorrow"). When the
/// scheduled time arrives the NPC is moved to the agreed location and the player is
/// notified, so the promise actually happens instead of being an empty one.
/// </summary>
public sealed class MeetingManager
{
    private const string SaveDataKey = "valewake-meetings";
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private MeetingSaveData data = new();

    public MeetingManager(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
    }

    public void Load()
    {
        data = helper.Data.ReadSaveData<MeetingSaveData>(SaveDataKey) ?? new MeetingSaveData();
        foreach (ScheduledMeeting meeting in data.Meetings.Where(m => m.State == MeetingStates.Active))
        {
            meeting.State = MeetingStates.Missed;
            meeting.LastMessage = "The game was reloaded before the meeting happened.";
        }
        Save();
    }

    private void Save() => helper.Data.WriteSaveData(SaveDataKey, data);

    public bool HasScheduledMeeting(string npcName) =>
        data.Meetings.Any(m => m.NpcName == npcName && m.State == MeetingStates.Scheduled);

    public ActionProposalDecision Evaluate(
        AgentActionProposal proposal,
        NPC npc,
        string playerInput,
        AgentRelationshipContext relationship)
    {
        if (!config.EnableActionAgent)
            return ActionProposalDecision.Reject("Action Agent is disabled.");
        if (!Context.IsMainPlayer)
            return ActionProposalDecision.Reject("Only the multiplayer host can schedule meetings.");
        if (npc.Age == 2)
            return ActionProposalDecision.Reject("Child NPCs cannot schedule meetings.");
        if (!string.Equals(proposal.Disposition, "accept", StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject($"{npc.displayName} did not agree to the meeting.");
        if (HasScheduledMeeting(npc.Name))
            return ActionProposalDecision.Reject($"{npc.displayName} already has a scheduled meeting.");

        int minimumHearts = Math.Max(1, config.MinimumActionHearts);
        int hearts = 0;
        if (Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship))
            hearts = friendship.Points / 250;
        if (hearts < minimumHearts)
            return ActionProposalDecision.Reject($"Scheduling a meeting requires at least {minimumHearts} hearts.");
        if (relationship.Trust < config.MinimumActionTrust)
            return ActionProposalDecision.Reject($"Scheduling a meeting requires at least {config.MinimumActionTrust} Valewake trust.");

        string location = GetMeetingLocation(proposal);
        int time = GetMeetingTime(proposal);
        string topic = GetMeetingTopic(proposal);
        return ActionProposalDecision.Allow(
            $"约好明天 {FormatTime(time)} 在 {FormatLocation(location)} 见，聊「{topic}」？",
            1
        );
    }

    public ScheduledMeeting Accept(
        AgentActionProposal proposal,
        NPC npc,
        ActionProposalDecision decision,
        string sourceTurnId)
    {
        int dayOffset = Math.Max(1, GetMeetingDayOffset(proposal));
        ScheduledMeeting meeting = new()
        {
            MeetingId = $"meet_{Guid.NewGuid():N}"[..16],
            SourceTurnId = sourceTurnId,
            NpcName = npc.Name,
            Location = GetMeetingLocation(proposal),
            Day = Game1.Date.TotalDays + dayOffset,
            Time = GetMeetingTime(proposal),
            Topic = GetMeetingTopic(proposal),
            State = MeetingStates.Scheduled,
        };
        data.Meetings.Add(meeting);
        Save();
        monitor.Log(
            $"Scheduled meeting {meeting.MeetingId}: {meeting.NpcName} at {meeting.Location} " +
            $"day {meeting.Day} {meeting.Time} ({meeting.Topic}).",
            LogLevel.Info
        );
        return meeting;
    }

    public void Update()
    {
        if (!Context.IsWorldReady)
            return;
        bool changed = false;
        foreach (ScheduledMeeting meeting in data.Meetings)
        {
            if (meeting.State == MeetingStates.Scheduled)
            {
                if (Game1.Date.TotalDays > meeting.Day)
                {
                    meeting.State = MeetingStates.Missed;
                    meeting.LastMessage = "The scheduled day passed.";
                    changed = true;
                }
                else if (Game1.Date.TotalDays == meeting.Day && Game1.timeOfDay >= meeting.Time)
                {
                    ActivateMeeting(meeting);
                    changed = true;
                }
            }
            else if (meeting.State == MeetingStates.Active)
            {
                if (Game1.Date.TotalDays > meeting.Day ||
                    (Game1.Date.TotalDays == meeting.Day && Game1.timeOfDay >= meeting.Time + 200))
                {
                    meeting.State = MeetingStates.Done;
                    meeting.LastMessage = "The meeting window ended.";
                    changed = true;
                }
            }
        }
        if (changed)
            Save();
    }

    private void ActivateMeeting(ScheduledMeeting meeting)
    {
        NPC? npc = Game1.getCharacterFromName(meeting.NpcName);
        GameLocation? location = ResolveLocation(meeting.Location);
        if (npc is null || location is null)
        {
            meeting.State = MeetingStates.Missed;
            meeting.LastMessage = "The NPC or meeting location is unavailable.";
            return;
        }
        Game1.warpCharacter(npc, location, SafeWarpTile(location));
        meeting.State = MeetingStates.Active;
        meeting.LastMessage = "The NPC is waiting.";
        Game1.addHUDMessage(new HUDMessage(
            $"{npc.displayName} 在 {FormatLocation(meeting.Location)} 等你（{meeting.Topic}）。"
        ));
    }

    // ---- proposal parameter helpers ----

    private static string GetMeetingLocation(AgentActionProposal proposal)
    {
        string location = GetString(proposal.Parameters, "location");
        return string.IsNullOrWhiteSpace(location) ? "Town" : location;
    }

    private static int GetMeetingTime(AgentActionProposal proposal)
    {
        int time = GetInt(proposal.Parameters, "time_of_day", 1800);
        return time is >= 600 and < 2400 ? time : 1800;
    }

    private static int GetMeetingDayOffset(AgentActionProposal proposal) =>
        GetInt(proposal.Parameters, "day_offset", 1);

    private static string GetMeetingTopic(AgentActionProposal proposal)
    {
        string topic = GetString(proposal.Parameters, "topic");
        return string.IsNullOrWhiteSpace(topic) ? "见面" : topic;
    }

    private static string GetString(Dictionary<string, JsonElement> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out JsonElement element))
            return "";
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.ToString(),
            _ => ""
        };
    }

    private static int GetInt(Dictionary<string, JsonElement> parameters, string key, int fallback)
    {
        if (parameters.TryGetValue(key, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out int value))
        {
            return value;
        }
        return fallback;
    }

    // ---- location + formatting helpers ----

    private static GameLocation? ResolveLocation(string name)
    {
        string n = (name ?? "").Trim();
        if (string.IsNullOrEmpty(n))
            return null;
        GameLocation? direct = Game1.getLocationFromName(n);
        if (direct is not null)
            return direct;

        string lowered = n.ToLowerInvariant();
        string[] candidates = lowered switch
        {
            _ when lowered.Contains("beach") || lowered.Contains("海边") || lowered.Contains("海滩") =>
                new[] { "Beach" },
            _ when lowered.Contains("town") || lowered.Contains("镇上") || lowered.Contains("镇") =>
                new[] { "Town" },
            _ when lowered.Contains("farm") || lowered.Contains("农场") =>
                new[] { "Farm" },
            _ when lowered.Contains("mountain") || lowered.Contains("山") =>
                new[] { "Mountain" },
            _ when lowered.Contains("forest") || lowered.Contains("森林") =>
                new[] { "Forest" },
            _ when lowered.Contains("seed") || lowered.Contains("pierre") || lowered.Contains("店") || lowered.Contains("杂货") =>
                new[] { "SeedShop" },
            _ when lowered.Contains("saloon") || lowered.Contains("酒馆") || lowered.Contains("酒吧") =>
                new[] { "Saloon" },
            _ when lowered.Contains("mine") || lowered.Contains("矿") =>
                new[] { "Mine" },
            _ when lowered.Contains("library") || lowered.Contains("museum") || lowered.Contains("博物馆") =>
                new[] { "ArchaeologyHouse" },
            _ => new[] { "Town" },
        };
        foreach (string candidate in candidates)
        {
            GameLocation? resolved = Game1.getLocationFromName(candidate);
            if (resolved is not null)
                return resolved;
        }
        return null;
    }

    private static Vector2 SafeWarpTile(GameLocation location)
    {
        if (location.warps.Count > 0)
        {
            Warp warp = location.warps[0];
            return new Vector2(warp.X, warp.Y);
        }
        return new Vector2(1, 1);
    }

    private static string FormatTime(int time)
    {
        int hour = time / 100;
        int minute = time % 100;
        return $"{hour}:{minute:D2}";
    }

    private static string FormatLocation(string name)
    {
        string n = (name ?? "").Trim();
        string lowered = n.ToLowerInvariant();
        if (lowered.Contains("beach") || lowered.Contains("海边") || lowered.Contains("海滩")) return "海边";
        if (lowered.Contains("town") || lowered.Contains("镇")) return "镇上";
        if (lowered.Contains("farm") || lowered.Contains("农场")) return "农场";
        if (lowered.Contains("mountain") || lowered.Contains("山")) return "山上";
        if (lowered.Contains("forest") || lowered.Contains("森林")) return "森林";
        if (lowered.Contains("seed") || lowered.Contains("pierre") || lowered.Contains("杂货") || lowered.Contains("店")) return "杂货店";
        if (lowered.Contains("saloon") || lowered.Contains("酒馆")) return "酒馆";
        if (lowered.Contains("mine") || lowered.Contains("矿")) return "矿洞";
        if (lowered.Contains("library") || lowered.Contains("museum") || lowered.Contains("博物馆")) return "博物馆";
        return n.Length == 0 ? "镇上" : n;
    }
}
