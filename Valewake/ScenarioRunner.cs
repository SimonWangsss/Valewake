using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;

namespace Valewake;

/// <summary>
/// Drives a deterministic, TestMode-only scenario across one or more in-game days.
/// It reuses the same chat + Evaluate + Accept chain as the real UI; the only
/// difference is that accepted action proposals are auto-confirmed instead of
/// showing a question dialogue.
/// </summary>
public sealed class ScenarioRunner
{
    private enum RunState
    {
        Idle,
        WaitingForDayStart,
        DaySetup,
        WaitingForChat,
        WaitingForJob,
        AdvancingDay,
        Done,
        Failed,
    }

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly ActionJobManager actionJobManager;
    private readonly MineExpeditionManager mineExpeditionManager;
    private readonly Func<NPC, string, Task> sendChatAsync;
    private readonly string reportPath;
    private readonly string doneMarkerPath;

    private ScenarioDefinition? scenario;
    private int dayIndex = -1;
    private string npcName = "";
    private RunState state = RunState.Idle;
    private long setupDoneTick;
    private long chatDeadlineTick;
    private long jobDeadlineTick;
    private string lastTurnId = "";
    private string lastReply = "";
    private string lastProposedAction = "";
    private bool lastAccepted;
    private string lastError = "";
    private readonly List<ScenarioDayResult> dayResults = new();

    public bool IsRunning =>
        state is not RunState.Idle and not RunState.Done and not RunState.Failed;

    public bool IsDone => state == RunState.Done;

    public ScenarioRunner(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        ActionJobManager actionJobManager,
        MineExpeditionManager mineExpeditionManager,
        Func<NPC, string, Task> sendChatAsync)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.actionJobManager = actionJobManager;
        this.mineExpeditionManager = mineExpeditionManager;
        this.sendChatAsync = sendChatAsync;

        string traces = Path.Combine(helper.DirectoryPath, "data", "traces");
        Directory.CreateDirectory(traces);
        reportPath = Path.Combine(traces, "scenario_report.json");
        doneMarkerPath = Path.Combine(traces, "scenario_done.marker");
    }

    /// <summary>Load the configured scenario file. Returns false on any parse/setup error.</summary>
    public bool TryStart()
    {
        string file = Path.Combine(helper.DirectoryPath, config.TestScenarioFile);
        if (!File.Exists(file))
        {
            monitor.Log($"ScenarioRunner: scenario file not found: {file}", LogLevel.Error);
            state = RunState.Failed;
            return false;
        }

        try
        {
            scenario = JsonSerializer.Deserialize<ScenarioDefinition>(
                File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (Exception ex)
        {
            monitor.Log($"ScenarioRunner: failed to parse scenario: {ex.Message}", LogLevel.Error);
            state = RunState.Failed;
            return false;
        }

        if (scenario is null || scenario.Days.Count == 0)
        {
            monitor.Log("ScenarioRunner: scenario has no days.", LogLevel.Error);
            state = RunState.Failed;
            return false;
        }

        npcName = string.IsNullOrWhiteSpace(scenario.Npc) ? "Emily" : scenario.Npc;
        dayIndex = -1;
        dayResults.Clear();
        state = RunState.WaitingForDayStart;
        monitor.Log(
            $"ScenarioRunner: loaded '{scenario.ScenarioId}' ({scenario.Days.Count} days, npc={npcName}).",
            LogLevel.Info
        );
        return true;
    }

    public void OnSaveLoaded()
    {
        if (state is RunState.Done or RunState.Failed)
            return;

        if (scenario is not null)
        {
            string save = Constants.SaveFolderName ?? "";
            if (scenario.SaveAllowlist.Count > 0 && !scenario.SaveAllowlist.Contains(save))
            {
                monitor.Log(
                    $"ScenarioRunner: save '{save}' is not in the allowlist; scenario disabled.",
                    LogLevel.Warn
                );
                state = RunState.Failed;
                return;
            }
            monitor.Log($"ScenarioRunner: save '{save}' is allowed.", LogLevel.Info);
        }
        else if (config.TestAutoRun)
        {
            TryStart();
        }
    }

    public void OnReturnedToTitle()
    {
        state = RunState.Idle;
        scenario = null;
        dayIndex = -1;
        dayResults.Clear();
    }

    public void OnDayStarted()
    {
        if (!IsRunning)
            return;

        dayIndex++;
        if (scenario is null || dayIndex >= scenario.Days.Count)
        {
            Finish();
            return;
        }

        ScenarioDay day = scenario.Days[dayIndex];
        ApplyDaySetup(day);
        setupDoneTick = Game1.ticks + 60;
        state = RunState.DaySetup;
        monitor.Log(
            $"ScenarioRunner: day {day.Day} (game day {Game1.Date.TotalDays}) setup at " +
            $"{Game1.timeOfDay}, location={day.Location}.",
            LogLevel.Info
        );
    }

    public void OnUpdateTicked()
    {
        if (!IsRunning || !Context.IsWorldReady)
            return;

        switch (state)
        {
            case RunState.DaySetup:
                if (Game1.ticks >= setupDoneTick)
                    BeginChat();
                break;

            case RunState.WaitingForChat:
                if (Game1.ticks > chatDeadlineTick)
                {
                    lastError = "chat timeout";
                    monitor.Log($"ScenarioRunner: chat timed out for {npcName}.", LogLevel.Warn);
                    FinishDayAndAdvance();
                }
                break;

            case RunState.WaitingForJob:
                if (!actionJobManager.HasActiveJob(npcName))
                {
                    FinishDayAndAdvance();
                }
                else if (Game1.ticks > jobDeadlineTick)
                {
                    actionJobManager.CancelAll("Scenario day timeout.");
                    lastError = "job timeout";
                    monitor.Log($"ScenarioRunner: job timed out for {npcName}.", LogLevel.Warn);
                    FinishDayAndAdvance();
                }
                break;
        }
    }

    /// <summary>Called by ModEntry when a scenario chat turn resolves on the main thread.</summary>
    public void NotifyChatResult(
        string npc,
        string turnId,
        string reply,
        string proposedAction,
        bool accepted)
    {
        if (state != RunState.WaitingForChat)
            return;

        lastTurnId = turnId;
        lastReply = reply;
        lastProposedAction = proposedAction;
        lastAccepted = accepted;
        lastError = "";

        if (accepted)
        {
            jobDeadlineTick = Game1.ticks + CurrentWaitTicks();
            state = RunState.WaitingForJob;
        }
        else
        {
            // No job accepted: the day is still a valid terminal observation.
            FinishDayAndAdvance();
        }
    }

    private void ApplyDaySetup(ScenarioDay day)
    {
        Game1.timeOfDay = day.Time;
        string location = string.IsNullOrWhiteSpace(day.Location) ? "Farm" : day.Location;
        Game1.warpFarmer(location, 64, 15, false);

        NPC? npc = ResolveNpc(npcName);
        if (npc is not null && npc.currentLocation is not Farm)
        {
            Farm farm = Game1.getFarm();
            Game1.warpCharacter(npc, farm, new Microsoft.Xna.Framework.Vector2(66, 18));
        }
    }

    private void BeginChat()
    {
        NPC? npc = ResolveNpc(npcName);
        if (npc is null)
        {
            lastError = $"NPC '{npcName}' not found";
            monitor.Log($"ScenarioRunner: {lastError}.", LogLevel.Warn);
            FinishDayAndAdvance();
            return;
        }

        ScenarioDay day = scenario!.Days[dayIndex];
        state = RunState.WaitingForChat;
        chatDeadlineTick = Game1.ticks + Math.Max(1200, config.BackendTimeoutSeconds * 60);
        monitor.Log($"ScenarioRunner: sending chat '{day.ChatInput}' to {npcName}.", LogLevel.Info);
        _ = sendChatAsync(npc, day.ChatInput);
    }

    private void FinishDayAndAdvance()
    {
        if (scenario is null || dayIndex >= scenario.Days.Count)
            return;

        ScenarioDay day = scenario.Days[dayIndex];
        ActionJob? job = actionJobManager.GetLastJob(npcName);
        ScenarioDayResult result = new()
        {
            DayIndex = day.Day,
            GameDay = Game1.Date.TotalDays,
            Time = Game1.timeOfDay,
            Location = Game1.currentLocation?.NameOrUniqueName ?? "",
            ChatInput = day.ChatInput,
            ExpectAction = day.ExpectAction,
            TurnId = lastTurnId,
            Reply = lastReply,
            ProposedAction = lastProposedAction,
            Accepted = lastAccepted,
            JobState = job?.State ?? "",
            CompletedTargets = job?.CompletedTargets ?? 0,
            FailedTargets = job?.FailedTargets ?? 0,
            TotalTargets = job?.Targets.Count ?? 0,
            Error = lastError,
        };
        dayResults.Add(result);
        monitor.Log(
            $"ScenarioRunner: day {day.Day} -> accepted={lastAccepted}, action={lastProposedAction}, " +
            $"jobState={result.JobState}, targets={result.CompletedTargets}/{result.TotalTargets}, error='{lastError}'.",
            LogLevel.Info
        );

        lastTurnId = "";
        lastReply = "";
        lastProposedAction = "";
        lastAccepted = false;
        lastError = "";

        if (dayIndex >= scenario.Days.Count - 1)
        {
            Finish();
            return;
        }

        state = RunState.AdvancingDay;
        AdvanceDay();
    }

    private int CurrentWaitTicks()
    {
        if (scenario is null || dayIndex >= scenario.Days.Count)
            return config.TestWaitTerminalTicks;
        return scenario.Days[dayIndex].WaitTerminalTicks > 0
            ? scenario.Days[dayIndex].WaitTerminalTicks
            : config.TestWaitTerminalTicks;
    }

    private void AdvanceDay()
    {
        try
        {
            // Passing out triggers the game's own end-of-day transition (Saved ->
            // DayStarted). It works reliably; the only caveat is a slow save on big
            // farms when crossing a season boundary, which completes on its own.
            Game1.player.startToPassOut();
            monitor.Log("ScenarioRunner: requested day advance via pass-out.", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            monitor.Log($"ScenarioRunner: day advance failed: {ex.Message}", LogLevel.Error);
            Finish();
        }
    }

    private void Finish()
    {
        state = RunState.Done;
        ScenarioReport report = new()
        {
            ScenarioId = scenario?.ScenarioId ?? "",
            Save = Constants.SaveFolderName ?? "",
            Npc = npcName,
            DaysRun = dayResults.Count,
            Accepted = dayResults.Count(r => r.Accepted),
            CompletedJobs = dayResults.Count(r => r.JobState == "completed"),
            Days = dayResults,
        };

        try
        {
            string save = Constants.SaveFolderName ?? "save";
            string saveReportPath = Path.Combine(
                Path.GetDirectoryName(reportPath) ?? ".",
                $"scenario_report_{save}.json"
            );
            File.WriteAllText(
                saveReportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
            );
            File.WriteAllText(doneMarkerPath, DateTime.UtcNow.ToString("O"));
            monitor.Log($"ScenarioRunner: finished. Report written to {saveReportPath}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            monitor.Log($"ScenarioRunner: failed to write report: {ex.Message}", LogLevel.Error);
        }
    }

    private NPC? ResolveNpc(string name)
    {
        NPC? npc = Game1.getCharacterFromName(name);
        if (npc is not null)
            return npc;
        foreach (GameLocation location in Game1.locations)
        {
            foreach (NPC candidate in location.characters)
            {
                if (candidate.Name == name)
                    return candidate;
            }
        }
        return null;
    }

    private sealed class ScenarioReport
    {
        public string ScenarioId { get; set; } = "";
        public string Save { get; set; } = "";
        public string Npc { get; set; } = "";
        public int DaysRun { get; set; }
        public int Accepted { get; set; }
        public int CompletedJobs { get; set; }
        public List<ScenarioDayResult> Days { get; set; } = new();
    }
}
