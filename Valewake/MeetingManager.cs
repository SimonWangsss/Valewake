using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.Locations;

namespace Valewake;

public static class MeetingStates
{
    public const string Scheduled = "scheduled";
    public const string Dispatching = "dispatching";
    public const string Arriving = "arriving";
    public const string Active = "active";
    public const string Done = "done";
    public const string Missed = "missed";
    public const string Returning = "returning";
    public const string Transferred = "transferred";
}

public sealed class MeetingSaveData
{
    public int SchemaVersion { get; set; } = 3;
    public List<ScheduledMeeting> Meetings { get; set; } = new();
}

public sealed class MeetingLocationSpots
{
    public List<MeetingTile> Fishing { get; set; } = new();
    public List<MeetingTile> Social { get; set; } = new();
}

public sealed class ScheduledMeeting
{
    public string MeetingId { get; set; } = "";
    public string SourceTurnId { get; set; } = "";
    public string ProposalId { get; set; } = "";
    public string NpcName { get; set; } = "";
    public string Location { get; set; } = "";
    public int Day { get; set; }
    public int Time { get; set; }
    public string Topic { get; set; } = "";
    public string State { get; set; } = MeetingStates.Scheduled;
    public string LastMessage { get; set; } = "";
    public ActionReturnContext? ReturnContext { get; set; }
    public ActionTile? ActualTile { get; set; }
    public bool InteractionObserved { get; set; }
    public long PlayerId { get; set; }
    public bool Following { get; set; }
    public int OriginalSpeed { get; set; } = 2;
    public float OriginalAddedSpeed { get; set; }
    public ActionTile? DestinationTile { get; set; }
    public ActionTile? ArrivalEntry { get; set; }
    public int EstimatedTravelMinutes { get; set; }
    public int StartedTime { get; set; }
    public bool LateNotified { get; set; }
    public string TransferredExpeditionId { get; set; } = "";
    public ActionReturnContext? ReturnGoal { get; set; }
    public string ReturnGoalReason { get; set; } = "";
    public int ReturnScheduleTime { get; set; } = -1;
    public string ReturnActivity { get; set; } = "";
    public string ReturnActivityMessage { get; set; } = "";
    [JsonIgnore] public long PathStartedTick { get; set; }
    [JsonIgnore] public MeetingTile? PathTarget { get; set; }
    [JsonIgnore] public string PathLocation { get; set; } = "";
    [JsonIgnore] public long ReturnStartedTick { get; set; }
    [JsonIgnore] public long NextCheckTick { get; set; }
    [JsonIgnore] public string LastWaitReason { get; set; } = "";
    [JsonIgnore] public string EndRequest { get; set; } = "";
    [JsonIgnore] public List<MeetingTile>? PlannedPath { get; set; }
    [JsonIgnore] public MeetingTile? LastFollowTile { get; set; }
    [JsonIgnore] public long LastProgressTick { get; set; }
    [JsonIgnore] public long LastRecoveryTick { get; set; } = -10000;
    [JsonIgnore] public string FollowMap { get; set; } = "";
    [JsonIgnore] public long LostContactTick { get; set; } = -1;
    [JsonIgnore] public Rectangle? LastBlockedBox { get; set; }
    [JsonIgnore] public ActionTile? RecoveryLanding { get; set; }
    [JsonIgnore] public long FollowNavigationStartedTick { get; set; }
    [JsonIgnore] public int LastConversationPauseMs { get; set; }
    [JsonIgnore] public Warp? PlannedExit { get; set; }
    [JsonIgnore] public MeetingTile? PlannedExitApproach { get; set; }
    [JsonIgnore] public string ExitSourceMap { get; set; } = "";
    [JsonIgnore] public string ExitDestinationMap { get; set; } = "";
    [JsonIgnore] public long ExitPlannedTick { get; set; }
    [JsonIgnore] public HashSet<string> RejectedExits { get; set; } = new();
    [JsonIgnore] public MeetingTile? ReturnLastTile { get; set; }
    [JsonIgnore] public string ReturnLastMap { get; set; } = "";
    [JsonIgnore] public long ReturnLastProgressTick { get; set; }
    [JsonIgnore] public bool ReturnBlockedNotified { get; set; }
    [JsonIgnore] public long ReturnReplanTick { get; set; }
    [JsonIgnore] public long ExitBlacklistTick { get; set; }
    [JsonIgnore] public bool ReturnMovementObserved { get; set; }
}

/// <summary>
/// Schedules and realizes in-game appointments that an NPC promises during dialogue
/// (for example Alex suggesting "meet me at the beach at 6pm tomorrow"). Travel
/// reserves the original schedule only at meeting time. Nearby players are
/// approached directly; separated companions use validated catch-up landings.
/// </summary>
public sealed class MeetingManager
{
    private static readonly HashSet<string> PublicLocations = new(StringComparer.OrdinalIgnoreCase)
        { "Town", "Beach", "Mountain", "Forest", "Farm" };
    private const string SaveDataKey = "valewake-meetings";
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private MeetingSaveData data = new();
    private Dictionary<string, MeetingLocationSpots> spots = new();
    public Func<string, bool> IsExternallyBusy { get; set; } = _ => false;

    public MeetingManager(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
    }

    public void Load()
    {
        if (!Context.IsMainPlayer) { data = new(); return; }
        try { spots = helper.Data.ReadJsonFile<Dictionary<string, MeetingLocationSpots>>("data/meeting_spots.json") ?? new(); }
        catch (Exception ex) { spots = new(); monitor.Log($"Could not load meeting spots: {ex.Message}", LogLevel.Warn); }
        data = helper.Data.ReadSaveData<MeetingSaveData>(SaveDataKey) ?? new MeetingSaveData();
        data.SchemaVersion = 3;
        foreach (ScheduledMeeting meeting in data.Meetings.Where(m => IsInProgress(m.State) || m.State == MeetingStates.Returning))
        {
            meeting.State = MeetingStates.Returning;
            meeting.LastMessage = "Restoring an interrupted meeting after reload.";
            Trace(meeting, "recovered_after_reload");
        }
        Save();
    }

    private void Save() => helper.Data.WriteSaveData(SaveDataKey, data);

    public bool HasScheduledMeeting(string npcName) =>
        data.Meetings.Any(m => m.NpcName == npcName && (m.State == MeetingStates.Scheduled || IsInProgress(m.State) || m.State == MeetingStates.Returning));

    public MeetingExpeditionHandoff? PrepareExpeditionHandoff(string npcName)
    {
        ScheduledMeeting? meeting = data.Meetings.LastOrDefault(m => m.NpcName == npcName &&
            MeetingRules.CanTransferToMine(m.State, m.PlayerId == 0 || m.PlayerId == Game1.player.UniqueMultiplayerID, m.ReturnContext is not null));
        return meeting is null ? null : MeetingExpeditionHandoff.Capture(meeting.MeetingId, meeting.ReturnContext!, meeting.OriginalSpeed, meeting.OriginalAddedSpeed);
    }

    public bool BlocksExpedition(string npcName) => HasScheduledMeeting(npcName) && PrepareExpeditionHandoff(npcName) is null;

    public void CompleteExpeditionHandoff(MeetingExpeditionHandoff handoff, string expeditionId)
    {
        ScheduledMeeting meeting = data.Meetings.Single(m => m.MeetingId == handoff.MeetingId);
        meeting.State = MeetingStates.Transferred;
        meeting.TransferredExpeditionId = expeditionId;
        meeting.Following = false;
        meeting.LastMessage = $"Meeting control transferred to mine expedition {expeditionId}; original return context preserved.";
        Save();
        Trace(meeting, "transferred_to_expedition");
    }

    public void ResumeAfterDialogue(string npcName)
    {
        NPC? npc = Game1.getCharacterFromName(npcName);
        foreach (ScheduledMeeting meeting in data.Meetings.Where(m => m.NpcName == npcName && m.State == MeetingStates.Active && m.Following))
        {
            meeting.LastProgressTick = Game1.ticks;
            meeting.FollowNavigationStartedTick = 0;
            meeting.PathTarget = null;
            if (npc is not null)
            {
                meeting.LastConversationPauseMs = npc.movementPause;
                npc.controller = null;
                NpcActivityReset.ReleaseConversationPause(npc, helper);
            }
        }
    }

    private static bool IsInProgress(string state) => state is MeetingStates.Dispatching or MeetingStates.Arriving or MeetingStates.Active;

    public bool HasActiveMeeting(string npcName) => data.Meetings.Any(m => m.NpcName == npcName && IsInProgress(m.State) &&
        (m.PlayerId == 0 || m.PlayerId == Game1.player.UniqueMultiplayerID));

    public bool CanJoinTravel(NPC npc) => Context.IsMainPlayer && PublicLocations.Contains(npc.currentLocation.Name) &&
        npc.currentLocation == Game1.player.currentLocation && MeetingRules.Distance(Tile(npc), Tile(Game1.player)) <= 4 &&
        data.Meetings.Any(m => m.NpcName == npc.Name && m.State == MeetingStates.Active && !m.Following &&
            (m.PlayerId == 0 || m.PlayerId == Game1.player.UniqueMultiplayerID));

    public bool JoinTravel(NPC npc)
    {
        if (!CanJoinTravel(npc)) return false;
        ScheduledMeeting meeting = data.Meetings.Last(m => m.NpcName == npc.Name && m.State == MeetingStates.Active && !m.Following &&
            (m.PlayerId == 0 || m.PlayerId == Game1.player.UniqueMultiplayerID));
        npc.controller = null;
        npc.Halt();
        meeting.State = MeetingStates.Active;
        meeting.Following = true;
        meeting.InteractionObserved = true;
        meeting.StartedTime = Game1.timeOfDay;
        meeting.LastMessage = "The player explicitly started the waiting meeting.";
        meeting.PathTarget = null;
        Trace(meeting, "meeting_join_accepted");
        Save();
        return true;
    }

    public object[] GetNpcFacts(string npcName) => data.Meetings.Where(m => m.NpcName == npcName).TakeLast(8)
        .Select(m => (object)new { npc = m.NpcName, meeting_id = m.MeetingId, turn_id = m.SourceTurnId,
            action = "schedule_meeting", state = m.State, game_day = m.Day, time_of_day = m.Time,
            location = m.Location, topic = m.Topic, interaction_observed = m.InteractionObserved,
            following = m.Following,
            estimated_travel_minutes = m.EstimatedTravelMinutes, started_time = m.StartedTime,
            message = m.LastMessage }).ToArray();

    public string GetSummary() => data.Meetings.Count == 0 ? "No meetings in this save." :
        string.Join(Environment.NewLine, data.Meetings.TakeLast(12).Select(m =>
            $"{m.MeetingId}: {m.NpcName}, day {m.Day} {FormatTime(m.Time)}, {m.Location}, {m.State}; {m.LastMessage}"));

    public void CancelAll()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer) return;
        foreach (ScheduledMeeting meeting in data.Meetings.Where(m => m.State == MeetingStates.Scheduled || IsInProgress(m.State)))
        {
            meeting.State = meeting.ReturnContext is not null ? MeetingStates.Returning : MeetingStates.Missed;
            meeting.LastMessage = "Cancelled by player; restoring an active meeting when unobserved.";
            Trace(meeting, "cancelled");
        }
        Save();
    }

    public void MarkInteraction(string npcName)
    {
        if (!Context.IsMainPlayer) return;
        NPC? npc = Game1.getCharacterFromName(npcName);
        if (npc is null || npc.currentLocation != Game1.player.currentLocation ||
            Math.Abs(npc.Tile.X - Game1.player.Tile.X) + Math.Abs(npc.Tile.Y - Game1.player.Tile.Y) > 3) return;
        ScheduledMeeting? meeting = data.Meetings.LastOrDefault(m => m.NpcName == npcName && m.State == MeetingStates.Active);
        if (meeting is null || meeting.InteractionObserved) return;
        meeting.InteractionObserved = true;
        meeting.Following = true;
        if (meeting.StartedTime == 0) meeting.StartedTime = Game1.timeOfDay;
        Save();
        Trace(meeting, "interaction_observed");
    }

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
        if (IsFestivalActive())
            return ActionProposalDecision.Reject("现在有活动，结束后再约吧。");
        if (npc.Age == 2)
            return ActionProposalDecision.Reject("Child NPCs cannot schedule meetings.");
        if (!string.Equals(proposal.Disposition, "accept", StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject(proposal.Disposition == "negotiate" ? "还在商量，尚未确认时间和地点。" : $"{npc.displayName} 还没有答应这次安排。");
        if (HasScheduledMeeting(npc.Name))
            return ActionProposalDecision.Reject($"{npc.displayName} already has a scheduled meeting.");
        if (IsExternallyBusy(npc.Name))
            return ActionProposalDecision.Reject("这会儿还有任务没结束，等忙完再约吧。");
        if (proposal.Confidence < config.MinimumActionConfidence || string.IsNullOrWhiteSpace(proposal.Evidence) ||
            !playerInput.Contains(proposal.Evidence, StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject("还没听清具体的见面安排，再确认一下吧。");

        int minimumHearts = Math.Max(1, config.MinimumActionHearts);
        int hearts = 0;
        if (Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship))
            hearts = friendship.Points / 250;
        if (hearts < minimumHearts)
            return ActionProposalDecision.Reject($"Scheduling a meeting requires at least {minimumHearts} hearts.");
        if (relationship.Trust < config.MinimumActionTrust)
            return ActionProposalDecision.Reject($"Scheduling a meeting requires at least {config.MinimumActionTrust} Valewake trust.");

        int dayOffset = GetMeetingDayOffset(proposal);
        if (!MeetingRules.CanSchedule(Game1.timeOfDay, GetMeetingTime(proposal), dayOffset, config.MeetingMinimumLeadMinutes))
            return ActionProposalDecision.Reject($"见面要在 8:00 到 22:00，今天至少提前 {Math.Max(10, config.MeetingMinimumLeadMinutes)} 分钟，换个时间吧。");
        if (IsFestivalDay(Game1.Date.TotalDays + dayOffset))
            return ActionProposalDecision.Reject($"那天有节日，{npc.displayName} 可能没空，改天再约吧。");

        string location = GetMeetingLocation(proposal);
        GameLocation? resolved = ResolveLocation(location);
        if (resolved is null || FindSafeTile(resolved, npc, GetMeetingTopic(proposal)).Tile is null)
            return ActionProposalDecision.Reject("那里暂时找不到能安全站立、方便你走近的位置，我们换个地方吧。");
        int time = GetMeetingTime(proposal);
        string topic = GetMeetingTopic(proposal);
        return ActionProposalDecision.Allow(
            $"约好{(dayOffset == 0 ? "今天" : dayOffset == 1 ? "明天" : $"{dayOffset}天后")} {FormatTime(time)} 在 {FormatLocation(location)} 见，聊「{topic}」？",
            1
        );
    }

    public ScheduledMeeting? Accept(
        AgentActionProposal proposal,
        NPC npc,
        ActionProposalDecision decision,
        string sourceTurnId)
    {
        int dayOffset = GetMeetingDayOffset(proposal);
        if (!Context.IsMainPlayer || !decision.Allowed || HasScheduledMeeting(npc.Name) || IsExternallyBusy(npc.Name) ||
            !MeetingRules.CanSchedule(Game1.timeOfDay, GetMeetingTime(proposal), dayOffset, config.MeetingMinimumLeadMinutes))
        {
            TraceDecision(proposal, npc, sourceTurnId, "confirmation_revalidation_failed", false, "Meeting conditions changed.");
            Game1.addHUDMessage(new HUDMessage("时间或安排已经变了，我们重新约一下吧。"));
            return null;
        }
        ScheduledMeeting meeting = new()
        {
            MeetingId = $"meet_{Guid.NewGuid():N}"[..16],
            SourceTurnId = sourceTurnId,
            ProposalId = proposal.ProposalId,
            NpcName = npc.Name,
            Location = ResolveLocation(GetMeetingLocation(proposal))?.NameOrUniqueName ?? GetMeetingLocation(proposal),
            Day = Game1.Date.TotalDays + dayOffset,
            Time = GetMeetingTime(proposal),
            Topic = GetMeetingTopic(proposal),
            State = MeetingStates.Scheduled,
            PlayerId = Game1.player.UniqueMultiplayerID,
        };
        data.Meetings.Add(meeting);
        Save();
        Trace(meeting, "scheduled");
        monitor.Log(
            $"Scheduled meeting {meeting.MeetingId}: {meeting.NpcName} at {meeting.Location} " +
            $"day {meeting.Day} {meeting.Time} ({meeting.Topic}).",
            LogLevel.Info
        );
        return meeting;
    }

    public void Update()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;
        if (!Game1.shouldTimePass() || IsFestivalActive())
        {
            foreach (ScheduledMeeting paused in data.Meetings.Where(m => m.State is MeetingStates.Active or MeetingStates.Returning))
            {
                paused.LastProgressTick = Game1.ticks;
                paused.ReturnLastProgressTick = Game1.ticks;
                paused.FollowNavigationStartedTick = 0;
                if (paused.LostContactTick >= 0) paused.LostContactTick = Game1.ticks;
            }
            return;
        }
        bool changed = false;
        foreach (ScheduledMeeting meeting in data.Meetings)
        {
            if (Game1.ticks < meeting.NextCheckTick) continue;
            meeting.NextCheckTick = Game1.ticks + (meeting.State == MeetingStates.Scheduled ? 120 : 30);
            if (meeting.State == MeetingStates.Scheduled)
            {
                if (MeetingRules.WindowExpired(Game1.Date.TotalDays, meeting.Day, Game1.timeOfDay, meeting.Time))
                {
                    meeting.State = MeetingStates.Missed;
                    meeting.LastMessage = "The scheduled arrival window passed.";
                    changed = true;
                    Trace(meeting, "missed");
                }
                else if (MeetingRules.MeetingDue(Game1.Date.TotalDays, meeting.Day, Game1.timeOfDay, meeting.Time) && Game1.timeOfDay < config.MeetingEndTime)
                {
                    if (IsExternallyBusy(meeting.NpcName))
                    {
                        changed |= NotifyLate(meeting);
                        continue;
                    }
                    changed |= ActivateAtTime(meeting);
                    if (meeting.State == MeetingStates.Scheduled) changed |= NotifyLate(meeting);
                }
            }
            else if (meeting.State is MeetingStates.Dispatching or MeetingStates.Arriving)
            {
                meeting.State = MeetingStates.Returning;
                meeting.LastMessage = "Legacy early travel was retired; restoring the original schedule.";
                Trace(meeting, "legacy_travel_retired");
                changed = true;
            }
            else if (meeting.State == MeetingStates.Active)
            {
                if (Game1.Date.TotalDays > meeting.Day || Game1.timeOfDay >= config.MeetingEndTime ||
                    (!meeting.Following && MeetingRules.Minutes(Game1.timeOfDay) >= MeetingRules.Minutes(meeting.Time) + 120))
                {
                    meeting.State = MeetingStates.Returning;
                    meeting.LastMessage = "The meeting window ended.";
                    changed = true;
                    Trace(meeting, "window_ended");
                }
                else changed |= UpdateCompanion(meeting);
            }
            else if (meeting.State == MeetingStates.Returning)
                changed |= TryRestore(meeting);
        }
        if (changed)
            Save();
    }

    private bool ActivateAtTime(ScheduledMeeting meeting)
    {
        NPC? npc = Game1.getCharacterFromName(meeting.NpcName);
        GameLocation? location = ResolveLocation(meeting.Location);
        if (npc is null || location is null) return false;
        Farmer? player = MeetingPlayer(meeting);
        bool nearby = player?.currentLocation == location && npc.currentLocation == location &&
            MeetingRules.Distance(Tile(npc), Tile(player)) <= Math.Clamp(config.MeetingApproachRadiusTiles, 3, 20);
        SafeSite site = FindSafeTile(location, npc, meeting.Topic);
        bool stayHere = npc.currentLocation == location && Walkable(location, npc, Tile(npc));
        if (!nearby && !stayHere && (site.Tile is null || Observed(npc.currentLocation, Tile(npc)) || Observed(location, site.Tile.Value)))
        {
            WaitOnce(meeting, "activation_deferred_observed_or_no_site");
            return false;
        }
        meeting.ReturnContext = new ActionReturnContext { LocationName = npc.currentLocation.NameOrUniqueName,
            TileX = (int)npc.Tile.X, TileY = (int)npc.Tile.Y, FacingDirection = npc.FacingDirection,
            FollowSchedule = npc.followSchedule };
        meeting.OriginalSpeed = npc.Speed;
        meeting.OriginalAddedSpeed = npc.addedSpeed;
        NpcActivityReset.Stop(npc, helper);
        npc.followSchedule = false;
        if (!nearby && !stayHere)
            Game1.warpCharacter(npc, location, new Vector2(site.Tile!.Value.X, site.Tile.Value.Y));
        meeting.State = MeetingStates.Active;
        meeting.Following = nearby;
        meeting.StartedTime = nearby ? Game1.timeOfDay : 0;
        meeting.ActualTile = new((int)npc.Tile.X, (int)npc.Tile.Y);
        meeting.DestinationTile = meeting.ActualTile;
        meeting.EstimatedTravelMinutes = 0;
        meeting.ArrivalEntry = null;
        meeting.LastProgressTick = Game1.ticks;
        meeting.LastMessage = nearby ? "Meeting time reached; approaching the nearby player." : "Meeting is ready; waiting for the player.";
        Trace(meeting, nearby ? "nearby_meeting_started" : "arrived", site);
        Game1.addHUDMessage(new HUDMessage($"{npc.displayName} {(nearby ? "来找你了" : "已在约定地图等你")}。"));
        return true;
    }

    private bool NotifyLate(ScheduledMeeting meeting)
    {
        if (!meeting.LateNotified && Game1.Date.TotalDays == meeting.Day && Game1.timeOfDay > meeting.Time)
        {
            meeting.LateNotified = true;
            meeting.LastMessage = "Departure or travel is delayed; no teleport to the meeting site.";
            Trace(meeting, "arrival_delayed");
            Game1.addHUDMessage(new HUDMessage($"{meeting.NpcName} 赴约耽搁了，可能稍晚一些。"));
            return true;
        }
        return false;
    }

    // ---- proposal parameter helpers ----

    private static string GetMeetingLocation(AgentActionProposal proposal)
    {
        string location = GetString(proposal.Parameters, "location");
        return string.IsNullOrWhiteSpace(location) ? "Town" : location;
    }

    private static int GetMeetingTime(AgentActionProposal proposal)
    {
        return GetInt(proposal.Parameters, "time_of_day", -1);
    }

    private static int GetMeetingDayOffset(AgentActionProposal proposal) =>
        GetInt(proposal.Parameters, "day_offset", 0);

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

    // ---- festival / occupancy helpers ----

    private HashSet<string>? festivalDates;

    private static bool IsFestivalActive() =>
        Game1.isFestival() || Game1.CurrentEvent is not null || Game1.eventUp;

    private bool IsFestivalDay(int totalDays)
    {
        festivalDates ??= LoadFestivalDates();
        int dayOfYear = totalDays % 112;
        int seasonIndex = dayOfYear / 28;
        int dayOfMonth = dayOfYear % 28 + 1;
        string season = seasonIndex switch { 0 => "spring", 1 => "summer", 2 => "fall", _ => "winter" };
        return festivalDates.Contains($"{season}_{dayOfMonth}");
    }

    private static HashSet<string> LoadFestivalDates()
    {
        HashSet<string> dates = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            Dictionary<string, string> calendar =
                Game1.content.Load<Dictionary<string, string>>("Data/Festivals/FestivalDates");
            foreach (string key in calendar.Keys)
                dates.Add(key);
        }
        catch (Exception)
        {
            // Festival data unavailable; degrade gracefully (no festival-day gating).
        }
        return dates;
    }

    // ---- location + formatting helpers ----

    private static GameLocation? ResolveLocation(string name)
    {
        string? canonical = MeetingRules.ResolveMeetingLocation(name);
        return canonical is null ? null : Game1.getLocationFromName(canonical);
    }

    private sealed record SafeSite(MeetingTile? Tile, MeetingTile? Approach, MeetingTile? Entry,
        int Checked, Dictionary<string, int> Rejections);

    private static bool Walkable(GameLocation location, NPC npc, MeetingTile tile, bool allowEdge = false)
    {
        int edge = allowEdge ? 0 : 1;
        if (tile.X < edge || tile.Y < edge || tile.X >= location.Map.Layers[0].LayerWidth - edge ||
            tile.Y >= location.Map.Layers[0].LayerHeight - edge) return false;
        return CanOccupy(location, npc, BoxAt(npc, tile));
    }

    private static bool CanOccupy(GameLocation map, NPC npc, Rectangle box, bool exitTransit = false)
    {
        if (box.Width <= 0 || box.Height <= 0) return false;
        foreach (MeetingTile p in MeetingRules.FootprintTiles(box.Left, box.Top, box.Width, box.Height))
        {
            if (p.X < 0 || p.Y < 0 || p.X >= map.Map.Layers[0].LayerWidth || p.Y >= map.Map.Layers[0].LayerHeight) return false;
            if (map.Map.GetLayer("Back")?.Tiles[p.X, p.Y] is null) return false;
            bool deck = map.doesTileHaveProperty(p.X, p.Y, "Passable", "Buildings") is not null;
            if (MeetingRules.BlocksWater(map.doesTileHaveProperty(p.X, p.Y, "Water", "Back") is not null, deck)) return false;
            if (map.doesTileHaveProperty(p.X, p.Y, "Passable", "Back") is not null) return false;
            if (map.Map.GetLayer("Buildings")?.Tiles[p.X, p.Y] is not null && !deck &&
                map.doesTileHaveProperty(p.X, p.Y, "Shadow", "Buildings") is null) return false;
            Vector2 tile = new(p.X, p.Y);
            if (map.Objects.TryGetValue(tile, out var obj) && !obj.isPassable()) return false;
            if (map.terrainFeatures.TryGetValue(tile, out var feature) && !feature.isPassable()) return false;
        }
        if (map.buildings.Any(building => building.intersects(box))) return false;
        // Only the short, declared exit corridor can use player-passable NPCBarrier tiles.
        return !map.isCollidingPosition(box, Game1.viewport, exitTransit, 0, false,
            exitTransit ? Game1.player : npc, true, skipCollisionEffects: true);
    }

    private SafeSite FindSafeTile(GameLocation location, NPC npc, string topic = "")
    {
        Dictionary<MeetingTile, bool> walkCache = new();
        bool CanWalk(MeetingTile p)
        {
            if (!walkCache.TryGetValue(p, out bool result)) walkCache[p] = result = Walkable(location, npc, p);
            return result;
        }
        List<MeetingTile> entries = new();
        if (Game1.player.currentLocation == location)
            entries.Add(new((int)Game1.player.Tile.X, (int)Game1.player.Tile.Y));
        else
            foreach (GameLocation other in Game1.locations)
                foreach (Warp warp in other.warps)
                    if (warp.TargetName == location.NameOrUniqueName || warp.TargetName == location.Name)
                        entries.Add(new(warp.TargetX, warp.TargetY));
        Dictionary<string, int> rejections = new();
        int checkedCount = 0;
        List<(MeetingTile Tile, MeetingTile Entry)> candidates = new();
        foreach (MeetingTile entry in entries.Distinct().Take(12))
        {
            // Entries may be boundary warp tiles; validate the immediately inward neighbors.
            MeetingTile? start = new[] { entry }.Concat(MeetingRules.Neighbors(entry))
                .Where(CanWalk).Select(p => (MeetingTile?)p).FirstOrDefault();
            if (start is null) continue;
            candidates.AddRange(MeetingRules.Reachable(start.Value, CanWalk).Select(p => (p, start.Value)));
        }
        bool fishing = MeetingRules.IsFishing(topic);
        MeetingTile[] anchors = spots.TryGetValue(location.Name, out var profile)
            ? (fishing ? profile.Fishing : profile.Social).Where(p => p.X >= 0 && p.Y >= 0 &&
                p.X < location.Map.Layers[0].LayerWidth && p.Y < location.Map.Layers[0].LayerHeight).ToArray()
            : Array.Empty<MeetingTile>();
        if (anchors.Length == 0) anchors = new[] { new MeetingTile(location.Map.Layers[0].LayerWidth / 2, location.Map.Layers[0].LayerHeight / 2) };
        int Score(MeetingTile p) => anchors.Min(a => MeetingRules.Distance(a, p)) * 4 - MeetingRules.Neighbors(p).Count(CanWalk);
        bool NearWater(MeetingTile p) => Enumerable.Range(-3, 7).Any(dx => Enumerable.Range(-3, 7).Any(dy =>
            location.doesTileHaveProperty(p.X + dx, p.Y + dy, "Water", "Back") is not null));
        foreach (var pair in candidates.DistinctBy(p => p.Tile).OrderBy(p => Score(p.Tile)))
            {
                MeetingTile candidate = pair.Tile;
                MeetingTile start = pair.Entry;
                checkedCount++;
                string reason = "";
                if (!CanWalk(candidate)) reason = "blocked";
                else if (fishing && !NearWater(candidate)) reason = "not_shoreline";
                else if (MeetingRules.Neighbors(candidate).Count(CanWalk) < 3) reason = "cramped";
                else if (location.Map.GetLayer("Front")?.Tiles[candidate.X, candidate.Y] is not null ||
                         location.Map.GetLayer("AlwaysFront")?.Tiles[candidate.X, candidate.Y] is not null ||
                         location.Map.GetLayer("Front")?.Tiles[candidate.X, Math.Max(0, candidate.Y - 1)] is not null) reason = "foreground_occlusion";
                else if (location.warps.Any(w => Math.Abs(w.X - candidate.X) + Math.Abs(w.Y - candidate.Y) < 3)) reason = "warp_zone";
                else if (location.characters.Any(c => c != npc && c.Tile == new Vector2(candidate.X, candidate.Y)) ||
                    location.farmers.Any(f => f.Tile == new Vector2(candidate.X, candidate.Y))) reason = "occupied";
                else if (!MeetingRules.HasApproach(candidate, start, CanWalk)) reason = "no_player_approach";
                if (reason.Length > 0) { rejections[reason] = rejections.GetValueOrDefault(reason) + 1; continue; }
                HashSet<MeetingTile> approachRegion = MeetingRules.Reachable(start, p => p != candidate && CanWalk(p));
                MeetingTile approach = MeetingRules.Neighbors(candidate).First(approachRegion.Contains);
                return new(candidate, approach, start, checkedCount, rejections);
            }
        return new(null, null, null, checkedCount, rejections);
    }

    public bool EndForNpc(string npcName, string request = "")
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer) return false;
        ScheduledMeeting? meeting = data.Meetings.LastOrDefault(m => m.NpcName == npcName && IsInProgress(m.State) &&
            (m.PlayerId == 0 || m.PlayerId == Game1.player.UniqueMultiplayerID));
        if (meeting is null) return false;
        meeting.State = MeetingStates.Returning;
        meeting.LastMessage = "The player ended the meeting.";
        meeting.EndRequest = request;
        meeting.ReturnStartedTick = Game1.ticks;
        meeting.ReturnLastProgressTick = Game1.ticks;
        meeting.ReturnLastTile = null;
        meeting.ReturnMovementObserved = false;
        meeting.ReturnGoal = null;
        meeting.PlannedExit = null;
        meeting.RejectedExits.Clear();
        meeting.PathTarget = null;
        NPC? npc = Game1.getCharacterFromName(npcName);
        if (npc is not null) { npc.controller = null; npc.Halt(); NpcActivityReset.ReleaseConversationPause(npc, helper); }
        Save();
        Trace(meeting, "player_ended");
        return true;
    }

    private bool UpdateCompanion(ScheduledMeeting meeting)
    {
        NPC? npc = Game1.getCharacterFromName(meeting.NpcName);
        Farmer? player = MeetingPlayer(meeting);
        if (npc is null || player is null) return false;
        npc.followSchedule = false;
        if (Game1.activeClickableMenu is not null || Game1.dialogueUp)
        {
            npc.controller = null;
            npc.Halt();
            meeting.PathTarget = null;
            meeting.FollowNavigationStartedTick = 0;
            meeting.LastProgressTick = Game1.ticks;
            if (meeting.LostContactTick >= 0) meeting.LostContactTick = Game1.ticks;
            return false;
        }
        if (!meeting.Following && npc.currentLocation == player.currentLocation &&
            MeetingRules.Distance(Tile(npc), Tile(player)) <= Math.Clamp(config.MeetingApproachRadiusTiles, 3, 20))
        {
            meeting.Following = true;
            meeting.StartedTime = Game1.timeOfDay;
            meeting.LastProgressTick = Game1.ticks;
            meeting.LastMessage = "The player arrived; following during the meeting.";
            Trace(meeting, "following_started");
            return true;
        }
        if (!meeting.Following) return false;
        if (!PublicLocations.Contains(player.currentLocation.Name)) return false;
        if (meeting.FollowMap != npc.currentLocation.NameOrUniqueName || meeting.LastFollowTile != Tile(npc))
        {
            meeting.FollowMap = npc.currentLocation.NameOrUniqueName;
            meeting.LastFollowTile = Tile(npc);
            meeting.LastProgressTick = Game1.ticks;
        }
        int distance = MeetingRules.Distance(Tile(npc), Tile(player));
        bool sameMap = npc.currentLocation == player.currentLocation;
        if (sameMap && distance <= Math.Max(2, config.MeetingFollowDistanceTiles))
        {
            npc.controller = null;
            npc.Halt();
            meeting.PathTarget = null;
            meeting.FollowNavigationStartedTick = 0;
            meeting.LastProgressTick = Game1.ticks;
            NpcActivityReset.ReleaseConversationPause(npc, helper);
            npc.faceDirection(MeetingRules.IdleFacing(Tile(player).X - Tile(npc).X, Tile(player).Y - Tile(npc).Y));
            return false;
        }
        if (meeting.FollowNavigationStartedTick == 0)
        {
            meeting.LastConversationPauseMs = Math.Max(meeting.LastConversationPauseMs, npc.movementPause);
            meeting.FollowNavigationStartedTick = Game1.ticks;
            meeting.LastProgressTick = Game1.ticks;
            NpcActivityReset.ReleaseConversationPause(npc, helper);
            Trace(meeting, "follow_walk_resumed");
        }
        if (sameMap) meeting.LostContactTick = -1;
        else if (meeting.LostContactTick < 0) meeting.LostContactTick = Game1.ticks;
        long recoveryAge = Game1.ticks - (sameMap ? meeting.LastProgressTick : meeting.LostContactTick);
        if ((!sameMap || MeetingRules.RecoveryGracePassed(meeting.FollowNavigationStartedTick, Game1.ticks, config.MeetingFollowRecoveryGraceTicks)) &&
            MeetingRules.CatchUpDue(sameMap, distance, recoveryAge,
                Game1.ticks - meeting.LastRecoveryTick, config.MeetingCatchUpDistanceTiles,
                config.MeetingStallRecoveryTicks, config.MeetingCrossMapCatchUpTicks, config.MeetingCatchUpCooldownTicks) &&
            TryFollowRecovery(meeting, npc, player, sameMap ? "distance_or_stall" : "map_change")) return true;
        if (npc.currentLocation != player.currentLocation)
        {
            TravelToward(meeting, npc, player.currentLocation, Tile(player));
            return false;
        }
        MeetingTile? destination = OpenNear(npc.currentLocation, npc, Tile(player), 3);
        if (destination is not null) StartPath(meeting, npc, destination.Value);
        return false;
    }

    private static Farmer? MeetingPlayer(ScheduledMeeting meeting) => meeting.PlayerId == 0 ? Game1.player :
        Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == meeting.PlayerId);

    private bool TryFollowRecovery(ScheduledMeeting meeting, NPC npc, Farmer player, string reason)
    {
        GameLocation map = player.currentLocation;
        Dictionary<MeetingTile, bool> cache = new();
        bool Ground(MeetingTile p)
        {
            if (!cache.TryGetValue(p, out bool value)) cache[p] = value = Walkable(map, npc, p);
            return value;
        }
        // Select from the player's connected component, not the stranded NPC's component.
        MeetingTile? landing = MeetingRules.RecoveryTiles(Tile(player), Ground,
                p => Occupied(map, npc, p) || map.warps.Any(w => MeetingRules.Distance(p, new(w.X, w.Y)) < 2))
            .OrderBy(p => Observed(map, p)).ThenBy(p => MeetingRules.Distance(p, Tile(player)))
            .Select(p => (MeetingTile?)p).FirstOrDefault();
        meeting.LastRecoveryTick = Game1.ticks;
        meeting.RecoveryLanding = landing is null ? null : new(landing.Value.X, landing.Value.Y);
        if (landing is null) { WaitOnce(meeting, "catchup_no_safe_landing"); return false; }
        // Intentionally allow a visible rescue if no hidden safe tile exists; never fake a walked path.
        Trace(meeting, "catchup_before_" + reason);
        NpcActivityReset.Stop(npc, helper);
        Game1.warpCharacter(npc, map, new Vector2(landing.Value.X, landing.Value.Y));
        meeting.PathTarget = null;
        meeting.LastFollowTile = landing;
        meeting.FollowMap = map.NameOrUniqueName;
        meeting.LastProgressTick = Game1.ticks;
        meeting.LostContactTick = -1;
        meeting.LastMessage = "Follow recovery teleported to validated ground connected to the player.";
        Trace(meeting, "follow_catchup_" + reason);
        return true;
    }

    private static MeetingTile Tile(Character c) => new((int)c.Tile.X, (int)c.Tile.Y);

    private static bool Occupied(GameLocation map, NPC npc, MeetingTile tile) =>
        map.characters.Any(c => c != npc && c.GetBoundingBox().Intersects(BoxAt(npc, tile))) ||
        map.farmers.Any(f => f.GetBoundingBox().Intersects(BoxAt(npc, tile)));

    private static Rectangle BoxAt(NPC npc, MeetingTile tile)
    {
        Rectangle box = npc.GetBoundingBox();
        box.Offset(tile.X * 64 - (int)npc.Position.X, tile.Y * 64 - (int)npc.Position.Y);
        return box;
    }

    private static MeetingTile? OpenNear(GameLocation map, NPC npc, MeetingTile center, int radius, Func<MeetingTile, bool>? filter = null, bool requireReachable = true)
    {
        Dictionary<MeetingTile, bool> cache = new();
        bool Ground(MeetingTile p)
        {
            if (!cache.TryGetValue(p, out bool result)) cache[p] = result = Walkable(map, npc, p);
            return result;
        }
        HashSet<MeetingTile>? reachable = requireReachable && map == npc.currentLocation ? MeetingRules.Reachable(Tile(npc), Ground) : null;
        return Enumerable.Range(-radius, radius * 2 + 1)
            .SelectMany(dx => Enumerable.Range(-radius, radius * 2 + 1).Select(dy => new MeetingTile(center.X + dx, center.Y + dy)))
            .Where(p => Ground(p) && !Occupied(map, npc, p) && (reachable is null || reachable.Contains(p)) && (filter is null || filter(p)))
            .OrderBy(p => MeetingRules.Distance(p, center)).ThenBy(p => MeetingRules.Distance(p, Tile(npc)))
            .Select(p => (MeetingTile?)p).FirstOrDefault();
    }

    private void StartPath(ScheduledMeeting meeting, NPC npc, MeetingTile target)
    {
        Farmer? owner = MeetingPlayer(meeting);
        npc.Speed = meeting.Following && owner is not null
            ? MeetingRules.FollowSpeed(owner.getMovementSpeed(), MeetingRules.Distance(Tile(npc), Tile(owner)),
                config.MeetingFollowSpeedBoost, config.MeetingFollowMaxSpeed, config.MeetingCatchUpMaxSpeed)
            : Math.Max(2, meeting.OriginalSpeed);
        npc.addedSpeed = meeting.Following ? 0 : meeting.OriginalAddedSpeed;
        if (npc.controller is not null && meeting.PathLocation == npc.currentLocation.NameOrUniqueName &&
            meeting.PathTarget == target && Game1.ticks - meeting.PathStartedTick < 240) return;
        if (Game1.ticks - meeting.PathStartedTick < (meeting.Following ? Math.Clamp(config.MeetingFollowRepathTicks, 15, 120) : 60)) return;
        NpcActivityReset.ReleaseConversationPause(npc, helper);
        meeting.PathTarget = target;
        meeting.PathLocation = npc.currentLocation.NameOrUniqueName;
        meeting.PathStartedTick = Game1.ticks;
        GameLocation map = npc.currentLocation;
        meeting.PlannedPath = GetPath(map, npc, Tile(npc), target, meeting.State == MeetingStates.Returning);
        if (meeting.PlannedPath is null)
        {
            npc.controller = null;
            npc.Halt();
            Trace(meeting, "path_unavailable");
            return;
        }
        npc.controller = new MeetingPathController(meeting.PlannedPath, map, npc,
            p => Walkable(map, npc, p), box => CanOccupy(map, npc, box), box =>
            {
                meeting.LastBlockedBox = box;
                Trace(meeting, "path_collision_guard");
            });
        Trace(meeting, "path_started");
    }

    private static List<MeetingTile>? GetPath(GameLocation map, NPC npc, MeetingTile start, MeetingTile target, bool avoidPeople = false)
    {
        Dictionary<MeetingTile, bool> cache = new();
        bool Ground(MeetingTile p)
        {
            if (!cache.TryGetValue(p, out bool result)) cache[p] = result = Walkable(map, npc, p) && (!avoidPeople || p == start || !Occupied(map, npc, p));
            return result;
        }
        return MeetingRules.FindPath(start, target, Ground);
    }

    private static bool Observed(GameLocation map, MeetingTile tile)
    {
        Rectangle point = new(tile.X * 64 - 64, tile.Y * 64 - 128, 192, 256);
        foreach (Farmer farmer in map.farmers)
        {
            // Other clients' viewports are unknown, so treat their whole location as observed.
            if (farmer != Game1.player) return true;
            Rectangle viewport = new(Game1.viewport.X, Game1.viewport.Y, Game1.viewport.Width, Game1.viewport.Height);
            viewport.Inflate(128, 128);
            if (viewport.Intersects(point)) return true;
        }
        return false;
    }

    private static IEnumerable<Warp> Exits(GameLocation map, NPC npc)
    {
        foreach (Warp warp in map.warps) yield return warp;
        foreach (var door in map.doors.Pairs)
        {
            Warp? warp = map.getWarpFromDoor(door.Key, npc);
            if (warp is not null) yield return warp;
        }
        foreach (var building in map.buildings)
        {
            if (!building.HasIndoors()) continue;
            Warp? warp = map.getWarpFromDoor(building.getPointForHumanDoor(), npc);
            if (warp is not null) yield return warp;
        }
    }

    private void PlanExit(ScheduledMeeting meeting, NPC npc, GameLocation destination)
    {
        GameLocation source = npc.currentLocation;
        meeting.PlannedExit = null;
        meeting.PlannedExitApproach = null;
        meeting.ExitSourceMap = source.NameOrUniqueName;
        meeting.ExitDestinationMap = destination.NameOrUniqueName;
        meeting.ExitPlannedTick = Game1.ticks;
        Dictionary<MeetingTile, bool> cache = new();
        bool Ground(MeetingTile p)
        {
            if (!cache.TryGetValue(p, out bool value)) cache[p] = value = Walkable(source, npc, p) && (p == Tile(npc) || !Occupied(source, npc, p));
            return value;
        }
        HashSet<MeetingTile> reachable = MeetingRules.Reachable(Tile(npc), Ground);
        Dictionary<string, string[]> graph = new(StringComparer.Ordinal);
        Dictionary<string, int> distances = new(StringComparer.Ordinal);
        IEnumerable<string> Neighbors(string name)
        {
            if (graph.TryGetValue(name, out string[]? existing)) return existing;
            GameLocation? map = Game1.getLocationFromName(name);
            string[] next = map is null ? Array.Empty<string>() : Exits(map, npc)
                .Select(w => Game1.getLocationFromName(w.TargetName)?.NameOrUniqueName)
                .Where(n => n is not null).Cast<string>().ToArray();
            graph[name] = next;
            return next;
        }
        int Hops(string next)
        {
            if (!distances.TryGetValue(next, out int distance)) distances[next] = distance =
                MeetingRules.MapDistance(next, destination.NameOrUniqueName, Neighbors, source.NameOrUniqueName);
            return distance;
        }
        var candidates = Exits(source, npc).Select(w => new { Exit = w, Next = Game1.getLocationFromName(w.TargetName) })
            .Where(c => c.Next is not null && !meeting.RejectedExits.Contains(ExitKey(c.Exit)))
            .Select(c => new { c.Exit, Hops = Hops(c.Next!.NameOrUniqueName) })
            .Where(c => c.Hops < int.MaxValue).OrderBy(c => c.Hops).ThenBy(c => MeetingRules.Distance(Tile(npc), new(c.Exit.X, c.Exit.Y)));
        foreach (var candidate in candidates)
        {
            MeetingTile? approach = GateApproach(source, npc, candidate.Exit, reachable);
            if (approach is null) continue;
            meeting.PlannedExit = candidate.Exit;
            meeting.PlannedExitApproach = approach;
            Trace(meeting, "map_exit_selected");
            return;
        }
        WaitOnce(meeting, "no_reachable_map_exit");
    }

    private static string ExitKey(Warp exit) => $"{exit.X},{exit.Y}:{exit.TargetName}:{exit.TargetX},{exit.TargetY}";

    private static MeetingTile? GateApproach(GameLocation map, NPC npc, Warp exit, HashSet<MeetingTile> reachable)
    {
        MeetingTile gate = new(exit.X, exit.Y);
        bool boundary = gate.X <= 1 || gate.Y <= 1 || gate.X >= map.Map.Layers[0].LayerWidth - 2 || gate.Y >= map.Map.Layers[0].LayerHeight - 2;
        return MeetingRules.ExitApproaches(gate, map.Map.Layers[0].LayerWidth, map.Map.Layers[0].LayerHeight,
            p => reachable.Contains(p) && !Occupied(map, npc, p),
            p => CanOccupy(map, npc, BoxAt(npc, p), boundary)).Select(p => (MeetingTile?)p).FirstOrDefault();
    }

    private bool TravelToward(ScheduledMeeting meeting, NPC npc, GameLocation destination, MeetingTile finalTile, bool hideArrival = false)
    {
        if (npc.currentLocation == destination)
        {
            MeetingTile? goal = OpenNear(destination, npc, finalTile, 4);
            if (goal is null) return false;
            if (MeetingRules.Distance(Tile(npc), goal.Value) == 0) return true;
            StartPath(meeting, npc, goal.Value);
            return false;
        }
        if (meeting.ExitSourceMap != npc.currentLocation.NameOrUniqueName || meeting.ExitDestinationMap != destination.NameOrUniqueName)
        {
            meeting.PlannedExit = null;
            meeting.RejectedExits.Clear();
        }
        if ((meeting.PlannedExit is null && Game1.ticks - meeting.ExitPlannedTick >= 120) ||
            meeting.ExitSourceMap != npc.currentLocation.NameOrUniqueName || meeting.ExitDestinationMap != destination.NameOrUniqueName)
            PlanExit(meeting, npc, destination);
        Warp? exit = meeting.PlannedExit;
        MeetingTile? approach = meeting.PlannedExitApproach;
        if (exit is null || approach is null) return false;
        if (Tile(npc) != approach.Value)
        {
            StartPath(meeting, npc, approach.Value);
            return false;
        }
        GameLocation? nextMap = Game1.getLocationFromName(exit.TargetName);
        if (nextMap is null) return false;
        MeetingTile? arrival = OpenNear(nextMap, npc, new(exit.TargetX, exit.TargetY), 2);
        if (arrival is null) { WaitOnce(meeting, "arrival_blocked"); return false; }
        if (hideArrival && Observed(nextMap, arrival.Value))
        {
            npc.controller = null;
            npc.Halt();
            WaitOnce(meeting, "arrival_entry_observed");
            return false;
        }
        npc.controller = null;
        npc.Halt();
        Game1.warpCharacter(npc, nextMap, new Vector2(arrival.Value.X, arrival.Value.Y));
        npc.followSchedule = false;
        meeting.PathTarget = null;
        meeting.PlannedExit = null;
        meeting.ExitPlannedTick = 0;
        Trace(meeting, "crossed_map_exit");
        return false;
    }

    private void WaitOnce(ScheduledMeeting meeting, string reason)
    {
        if (meeting.LastWaitReason == reason) return;
        meeting.LastWaitReason = reason;
        Trace(meeting, reason);
    }

    private bool SelectReturnGoal(ScheduledMeeting meeting, NPC npc)
    {
        ActionReturnContext origin = meeting.ReturnContext!;
        SchedulePathDescription? schedule = null;
        int key = origin.FollowSchedule && !npc.ignoreScheduleToday && npc.Schedule is not null
            ? MeetingRules.LatestScheduleTime(npc.Schedule.Keys, Game1.timeOfDay) : -1;
        if (key >= 0) npc.Schedule!.TryGetValue(key, out schedule);
        ActionReturnContext goal = origin;
        string reason = "original_position";
        GameLocation? scheduledMap = string.IsNullOrWhiteSpace(schedule?.targetLocationName) ? null : Game1.getLocationFromName(schedule.targetLocationName);
        if (schedule is not null && scheduledMap is not null && schedule.targetTile.X >= 0 && schedule.targetTile.Y >= 0 &&
            schedule.targetTile.X < scheduledMap.Map.Layers[0].LayerWidth && schedule.targetTile.Y < scheduledMap.Map.Layers[0].LayerHeight)
        {
            goal = new ActionReturnContext { LocationName = schedule.targetLocationName, TileX = schedule.targetTile.X,
                TileY = schedule.targetTile.Y, FacingDirection = schedule.facingDirection, FollowSchedule = origin.FollowSchedule };
            reason = "current_schedule";
        }
        else if (origin.FollowSchedule && !npc.ignoreScheduleToday)
        {
            try
            {
                GameLocation home = npc.getHome();
                Point entry = home is FarmHouse house ? house.getEntryLocation() :
                    new Point((int)npc.DefaultPosition.X / 64, (int)npc.DefaultPosition.Y / 64);
                goal = new ActionReturnContext { LocationName = home.NameOrUniqueName, TileX = entry.X, TileY = entry.Y,
                    FacingDirection = origin.FacingDirection, FollowSchedule = origin.FollowSchedule };
                reason = "home_without_schedule";
            }
            catch (Exception) { reason = "original_position_home_unavailable"; }
        }
        if (meeting.ReturnGoal is not null && meeting.ReturnGoal.LocationName == goal.LocationName &&
            meeting.ReturnGoal.TileX == goal.TileX && meeting.ReturnGoal.TileY == goal.TileY && meeting.ReturnScheduleTime == key) return false;
        meeting.ReturnGoal = goal;
        meeting.ReturnGoalReason = reason;
        meeting.LastMessage = $"Returning on foot to {goal.LocationName} ({reason}); the original schedule resumes on arrival.";
        meeting.ReturnScheduleTime = key;
        meeting.ReturnActivity = reason == "current_schedule" ? schedule!.endOfRouteBehavior ?? "" : "";
        meeting.ReturnActivityMessage = reason == "current_schedule" ? schedule!.endOfRouteMessage ?? "" : "";
        meeting.PlannedExit = null;
        meeting.RejectedExits.Clear();
        meeting.PathTarget = null;
        meeting.ReturnLastProgressTick = Game1.ticks;
        meeting.ReturnReplanTick = Game1.ticks;
        meeting.ExitBlacklistTick = 0;
        meeting.ReturnBlockedNotified = false;
        npc.controller = null;
        Trace(meeting, "return_destination_selected");
        return true;
    }

    private bool TryRestore(ScheduledMeeting meeting)
    {
        meeting.Following = false;
        NPC? npc = Game1.getCharacterFromName(meeting.NpcName);
        ActionReturnContext? origin = meeting.ReturnContext;
        if (npc is null || origin is null)
        {
            if (npc is not null) { npc.followSchedule = origin?.FollowSchedule ?? true; npc.checkSchedule(Game1.timeOfDay); }
            meeting.State = MeetingStates.Missed;
            meeting.LastMessage = "Original meeting return context unavailable.";
            Trace(meeting, "restore_failed");
            return true;
        }
        if (Game1.activeClickableMenu is not null || Game1.dialogueUp)
        {
            meeting.ReturnLastProgressTick = Game1.ticks;
            return false;
        }
        bool changed = SelectReturnGoal(meeting, npc);
        ActionReturnContext goal = meeting.ReturnGoal!;
        GameLocation? location = Game1.getLocationFromName(goal.LocationName);
        if (location is null) { WaitOnce(meeting, "return_destination_unavailable"); return changed; }
        if (meeting.ReturnStartedTick == 0) meeting.ReturnStartedTick = Game1.ticks;
        npc.followSchedule = false;
        if (meeting.ReturnLastProgressTick == 0 || meeting.ReturnLastMap != npc.currentLocation.NameOrUniqueName || meeting.ReturnLastTile != Tile(npc))
        {
            if (!meeting.ReturnMovementObserved && meeting.ReturnLastTile is not null && meeting.ReturnLastMap == npc.currentLocation.NameOrUniqueName && meeting.ReturnLastTile != Tile(npc))
            {
                meeting.ReturnMovementObserved = true;
                Trace(meeting, "return_walk_started");
            }
            meeting.ReturnLastProgressTick = Game1.ticks;
            meeting.ReturnLastMap = npc.currentLocation.NameOrUniqueName;
            meeting.ReturnLastTile = Tile(npc);
            meeting.ReturnBlockedNotified = false;
        }
        long stalled = Game1.ticks - meeting.ReturnLastProgressTick;
        if (stalled >= 240 && Game1.ticks - meeting.ReturnReplanTick >= 240)
        {
            if (meeting.PlannedExit is not null)
            {
                meeting.RejectedExits.Add(ExitKey(meeting.PlannedExit));
                if (meeting.ExitBlacklistTick == 0) meeting.ExitBlacklistTick = Game1.ticks;
            }
            if (meeting.ExitBlacklistTick > 0 && Game1.ticks - meeting.ExitBlacklistTick >= 600)
            {
                meeting.RejectedExits.Clear();
                meeting.ExitBlacklistTick = Game1.ticks;
            }
            meeting.ReturnReplanTick = Game1.ticks;
            meeting.PlannedExit = null;
            meeting.PathTarget = null;
            npc.controller = null;
            npc.Halt();
            Trace(meeting, "return_route_replan");
        }
        if (stalled >= 300 && !meeting.ReturnBlockedNotified && npc.currentLocation == Game1.player.currentLocation)
        {
            meeting.ReturnBlockedNotified = true;
            Game1.addHUDMessage(new HUDMessage($"{npc.displayName} 的返程路暂时被挡住了，请留出通道。"));
            Trace(meeting, "return_blocked_notice");
        }
        MeetingTile target = new(goal.TileX, goal.TileY);
        if (!TravelToward(meeting, npc, location, target))
        {
            if (stalled > Math.Max(1800, config.ActionPathTimeoutTicks) &&
                !npc.currentLocation.farmers.Any() && !location.farmers.Any())
            {
                MeetingTile? landing = OpenNear(location, npc, target, 4, requireReachable: false);
                if (landing is null) return changed;
                npc.controller = null;
                npc.Halt();
                Game1.warpCharacter(npc, location, new Vector2(landing.Value.X, landing.Value.Y));
                Trace(meeting, "offscreen_return_recovery");
            }
            else return changed;
        }
        NpcActivityReset.Stop(npc, helper);
        npc.Speed = meeting.OriginalSpeed;
        npc.addedSpeed = meeting.OriginalAddedSpeed;
        npc.FacingDirection = goal.FacingDirection is >= 0 and <= 3 ? goal.FacingDirection : Math.Clamp(origin.FacingDirection, 0, 3);
        npc.followSchedule = origin.FollowSchedule;
        if (npc.followSchedule)
        {
            npc.queuedSchedulePaths.Clear();
            npc.DirectionsToNewLocation = null;
            npc.lastAttemptedSchedule = Game1.timeOfDay;
        }
        meeting.State = MeetingStates.Done;
        meeting.LastMessage = "Walked back to the current schedule destination or safe adjacent ground; vanilla schedule resumed.";
        Trace(meeting, "schedule_restored");
        if (Tile(npc) == target && npc.followSchedule && (meeting.ReturnActivity.Length > 0 || meeting.ReturnActivityMessage.Length > 0))
        {
            try
            {
                npc.StartActivityRouteEndBehavior(meeting.ReturnActivity, meeting.ReturnActivityMessage);
                Trace(meeting, "return_activity_restored");
            }
            catch (Exception ex) { monitor.Log($"Could not restore {npc.Name}'s route activity: {ex.Message}", LogLevel.Warn); }
        }
        return true;
    }

    public void TraceDecision(AgentActionProposal proposal, NPC npc, string turnId, string eventName, bool allowed, string reason)
    {
        Trace(new ScheduledMeeting { SourceTurnId = turnId, ProposalId = proposal.ProposalId, NpcName = npc.Name,
            Location = GetMeetingLocation(proposal), Day = Game1.Date.TotalDays + GetMeetingDayOffset(proposal),
            Time = GetMeetingTime(proposal), LastMessage = reason }, eventName, allowed: allowed);
    }

    private void Trace(ScheduledMeeting meeting, string eventName, SafeSite? site = null, bool? allowed = null)
    {
        try
        {
            NPC? npc = Game1.getCharacterFromName(meeting.NpcName);
            Farmer? owner = MeetingPlayer(meeting);
            string path = Path.Combine(helper.DirectoryPath, "data", "traces", "meeting_trace.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(new { trace_schema = "meeting-v1",
                build_id = typeof(MeetingManager).Assembly.ManifestModule.ModuleVersionId,
                timestamp = DateTimeOffset.UtcNow, save_id = Constants.SaveFolderName,
                meeting_id = meeting.MeetingId, turn_id = meeting.SourceTurnId, proposal_id = meeting.ProposalId,
                npc_name = meeting.NpcName, action = "schedule_meeting", event_name = eventName, allowed,
                state = meeting.State, scheduled_day = meeting.Day, scheduled_time = meeting.Time,
                game_day = Game1.Date.TotalDays, game_time = Game1.timeOfDay, requested_location = meeting.Location,
                actual_location = npc?.currentLocation?.NameOrUniqueName, actual_tile = npc is null ? null : new ActionTile((int)npc.Tile.X, (int)npc.Tile.Y),
                site_validation = site is null ? null : new { candidate_tile = site.Tile,
                    approach_tile = site.Approach, entry_tile = site.Entry,
                    ground_walkable_and_water_free = site.Tile is not null,
                    player_approach_reachable = site.Approach is not null,
                    strategy = "activity_anchor_open_ground", fishing_shoreline_required = MeetingRules.IsFishing(meeting.Topic),
                    candidates_checked = site.Checked, rejected_reasons = site.Rejections },
                interaction_observed = meeting.InteractionObserved,
                following = meeting.Following, player_id = meeting.PlayerId,
                destination_tile = meeting.DestinationTile, arrival_entry = meeting.ArrivalEntry,
                estimated_travel_minutes = meeting.EstimatedTravelMinutes,
                departure_minute = Math.Max(360, MeetingRules.Minutes(meeting.Time) - meeting.EstimatedTravelMinutes),
                started_time = meeting.StartedTime, late_notified = meeting.LateNotified,
                arrival_strategy = "activate_at_time_no_early_travel",
                recovery_landing = meeting.RecoveryLanding,
                expedition_id = meeting.TransferredExpeditionId,
                npc_speed = npc?.Speed, npc_added_speed = npc?.addedSpeed, npc_facing = npc?.FacingDirection,
                conversation_pause_before_release_ms = meeting.LastConversationPauseMs,
                follow_navigation_age_ticks = meeting.FollowNavigationStartedTick == 0 ? 0 : Game1.ticks - meeting.FollowNavigationStartedTick,
                return_goal = meeting.ReturnGoal, return_goal_reason = meeting.ReturnGoalReason, return_schedule_time = meeting.ReturnScheduleTime,
                return_movement_observed = meeting.ReturnMovementObserved,
                return_stall_ticks = meeting.ReturnLastProgressTick == 0 ? 0 : Game1.ticks - meeting.ReturnLastProgressTick,
                exit_plan = meeting.PlannedExit is null ? null : new { source_map = meeting.ExitSourceMap,
                    destination_map = meeting.ExitDestinationMap, gate = new MeetingTile(meeting.PlannedExit.X, meeting.PlannedExit.Y),
                    approach = meeting.PlannedExitApproach, next_map = meeting.PlannedExit.TargetName },
                blocked_footprint = meeting.LastBlockedBox is Rectangle blocked ? new { blocked.X, blocked.Y, blocked.Width, blocked.Height } : null,
                follow_distance = npc is not null && owner is not null && npc.currentLocation == owner.currentLocation ? MeetingRules.Distance(Tile(npc), Tile(owner)) : (int?)null,
                end_request = meeting.EndRequest,
                path_target = meeting.PathTarget, path_location = meeting.PathLocation,
                path_length = meeting.PlannedPath?.Count,
                planned_path = eventName == "path_started" ? meeting.PlannedPath?.Take(256).ToArray() : null,
                path_truncated = meeting.PlannedPath?.Count > 256,
                schedule_restored = eventName == "schedule_restored" && npc?.followSchedule == meeting.ReturnContext?.FollowSchedule,
                message = meeting.LastMessage }) + Environment.NewLine);
        }
        catch (Exception ex) { monitor.Log($"Could not append Meeting Trace: {ex.Message}", LogLevel.Warn); }
    }

    private static string FormatTime(int time)
    {
        int hour = time / 100;
        int minute = time % 100;
        return $"{hour}:{minute:D2}";
    }

    private static string FormatLocation(string name)
    {
        string n = MeetingRules.ResolveMeetingLocation(name) ?? (name ?? "").Trim();
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
