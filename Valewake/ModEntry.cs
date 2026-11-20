using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Valewake;

/// <summary>Main SMAPI entry point for Valewake.</summary>
public sealed class ModEntry : Mod
{
    private ModConfig Config = new();
    private AgentBackendClient? backendClient;
    private BackendProcessManager? backendProcessManager;
    private RelationshipManager? relationshipManager;
    private ActionJobManager? actionJobManager;
    private MineExpeditionManager? mineExpeditionManager;
    private MeetingManager? meetingManager;
    private ScenarioRunner? scenarioRunner;
    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly List<AgentConversationMessage> conversationHistory = new();
    private int journalSession;
    private Task memorySessionReady = Task.CompletedTask;
    private NPC? activeChatNpc;
    private NPC? pendingVanillaNpc;
    private bool pendingVanillaDialogueSeen;
    private int pendingVanillaStartedTick;
    private string pendingVanillaLine = "";
    private string lastTurnId = "";
    private bool testAutoLoadDone;
    private int testAutoLoadCounter;
    private int testSaveQueueIndex;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        helper.WriteConfig(Config);

        if (!Config.EnableMod)
        {
            Monitor.Log("Valewake is disabled in config.json.", LogLevel.Info);
            return;
        }

        backendClient = new AgentBackendClient(Config.BackendUrl, Config.BackendTimeoutSeconds);
        backendProcessManager = new BackendProcessManager(Monitor, Config, helper.DirectoryPath);
        relationshipManager = new RelationshipManager(helper, Monitor, Config);
        actionJobManager = new ActionJobManager(helper, Monitor, Config);
        mineExpeditionManager = new MineExpeditionManager(helper, Monitor, Config);
        meetingManager = new MeetingManager(helper, Monitor, Config);
        VanillaDialogueObserver.Install(ModManifest.UniqueID + ".vanilla-dialogue", Monitor,
            entry => { _ = SendTraceEventSafeAsync("vanilla_dialogue_observed", new { entry.event_id, npc_name = entry.npc, entry.speaker,
                entry.kind, entry.source, entry.story_event_id, entry.text, entry.game_day, entry.game_time,
                save_id = Constants.SaveFolderName }); });
        NpcInteractionJournal.Install(ModManifest.UniqueID + ".gift-events", Monitor,
            entry => { _ = SendTraceEventSafeAsync("npc_gift_received", new { entry.event_id, entry.kind, entry.source,
                npc_name = entry.npc, entry.game_day, entry.game_time, entry.item_id, entry.item_name, entry.gift_taste,
                save_id = Constants.SaveFolderName, session_prefix = $"{Constants.SaveFolderName}:" }); });
        meetingManager.IsExternallyBusy = name => actionJobManager.HasActiveJob(name) || mineExpeditionManager.IsNpcBusy(name);
        actionJobManager.IsExternallyBusy = name => meetingManager.HasScheduledMeeting(name) || mineExpeditionManager.IsNpcBusy(name);
        mineExpeditionManager.IsExternallyBusy = name => meetingManager.BlocksExpedition(name);
        scenarioRunner = new ScenarioRunner(
            helper,
            Monitor,
            Config,
            actionJobManager,
            mineExpeditionManager,
            (npc, input) => SendChatToBackendAsync(input, npc, Array.Empty<AgentConversationMessage>(), false)
        );

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.Saved += OnSaved;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        AppDomain.CurrentDomain.ProcessExit += OnGameExiting;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Display.MenuChanged += OnMenuChanged;
        helper.Events.Display.RenderedWorld += OnRenderedWorld;

        helper.ConsoleCommands.Add(
            Config.StateCommandName,
            "Print the current AI agent game-state snapshot.",
            OnStateCommand
        );

        helper.ConsoleCommands.Add(
            Config.ChatCommandName,
            $"Send a message to the nearest supported NPC. Usage: {Config.ChatCommandName} <message>",
            OnChatCommand
        );

        helper.ConsoleCommands.Add("agent_npcs", "Export runtime NPC names for custom persona packs. Load a save first.",
            (_, _) =>
            {
                if (!Context.IsWorldReady)
                {
                    Monitor.Log("Load a save before exporting NPC names.", LogLevel.Warn);
                    return;
                }
                var entries = Utility.getAllCharacters().GroupBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).OrderBy(npc => npc.Name)
                    .Select(npc => new { npc_name = npc.Name, display_name = npc.displayName,
                        can_socialize = npc.CanSocialize, chat_enabled = IsConfiguredTarget(npc),
                        age_group = npc.Age switch { 2 => "child", 1 => "teen", _ => "adult" },
                        current_location = npc.currentLocation?.NameOrUniqueName ?? "" }).ToArray();
                Helper.Data.WriteJsonFile("data/npc_catalog.json", entries);
                foreach (var entry in entries)
                    Monitor.Log($"NPC '{entry.npc_name}' | {entry.display_name} | social={entry.can_socialize} | chat={entry.chat_enabled}", LogLevel.Info);
                Monitor.Log("NPC catalog exported to data/npc_catalog.json. Bind pack npc_name to the exact internal name, not display_name.", LogLevel.Info);
            });

        helper.ConsoleCommands.Add(
            "agent_jobs",
            "List active Valewake NPC jobs.",
            (_, _) => Monitor.Log(
                actionJobManager?.GetSummary() ?? "Action Job Manager is unavailable.",
                LogLevel.Info
            )
        );

        helper.ConsoleCommands.Add(
            "agent_cancel_jobs",
            "Cancel all active Valewake NPC jobs and restore their schedules.",
            (_, _) =>
            {
                if (!Context.IsWorldReady)
                {
                    Monitor.Log("Load a save before cancelling jobs.", LogLevel.Warn);
                    return;
                }
                actionJobManager?.CancelAll("Cancelled from the SMAPI console.");
                Monitor.Log("Active Valewake jobs cancelled.", LogLevel.Info);
            }
        );

        helper.ConsoleCommands.Add(
            "agent_mark",
            "Annotate the latest AI turn. Usage: agent_mark <keep|reject|boundary|memory_good|memory_bad|action_good|action_bad> [note]",
            OnMarkCommand
        );

        helper.ConsoleCommands.Add("agent_meetings", "List meetings; agent_meetings cancel cancels current meetings; agent_meetings end <NPC> ends an active meeting.",
            (_, args) =>
            {
                if (args.Length > 0 && args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase)) meetingManager?.CancelAll();
                if (args.Length > 1 && args[0].Equals("end", StringComparison.OrdinalIgnoreCase)) meetingManager?.EndForNpc(args[1]);
                Monitor.Log(meetingManager?.GetSummary() ?? "Meeting manager unavailable.", LogLevel.Info);
            });

        helper.ConsoleCommands.Add(
            "agent_expedition",
            "Show the active Valewake mine expedition.",
            (_, _) => Monitor.Log(mineExpeditionManager?.GetSummary() ?? "Mine Expedition Manager is unavailable.", LogLevel.Info)
        );

        helper.ConsoleCommands.Add(
            "agent_end_expedition",
            "End the active expedition; the NPC leaves after exiting a mine level.",
            (_, _) => mineExpeditionManager?.BeginReturn("The player ended the expedition.")
        );

        helper.ConsoleCommands.Add(
            "agent_mine_target",
            "Set the active companion's priority mine target to the tile under the cursor.",
            (_, _) => SetExpeditionTarget(Helper.Input.GetCursorPosition().GrabTile)
        );

        helper.ConsoleCommands.Add(
            "agent_setkey",
            "Set the LLM API key directly (avoids hand-editing config.json). Usage: agent_setkey <api-key>",
            (command, args) =>
            {
                if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
                {
                    Monitor.Log("Usage: agent_setkey <api-key>", LogLevel.Warn);
                    return;
                }
                string key = args[0].Trim();
                Config.LlmApiKey = key;
                Helper.WriteConfig(Config);
                WriteLlmConfigFile();
                _ = SyncLlmConfigToBackendAsync();
                string suffix = key.Length >= 4 ? key.Substring(key.Length - 4) : key;
                Monitor.Log($"API key saved ({key.Length} chars, ends '...{suffix}').", LogLevel.Info);
            }
        );

        Monitor.Log(
            "Valewake loaded. Use '" + Config.StateCommandName +
            "' for state or '" + Config.ChatCommandName +
            " <message>' near a social NPC. Right-click any supported NPC for continuous dialogue.",
            LogLevel.Info
        );
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGenericModConfigMenu();
        Monitor.Log($"Agent backend configured at: {Config.BackendUrl}", LogLevel.Info);
        // Persist config.json -> Backend/llm_config.json before the backend starts, so a
        // manually-edited config.json (full API key pasted with Notepad) is picked up.
        WriteLlmConfigFile();
        LogLlmKeySummary();
        _ = EnsureBackendReadyAndSyncConfigAsync();
    }

    private async Task EnsureBackendReadyAndSyncConfigAsync()
    {
        if (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync())
        {
            Monitor.Log("Agent backend is unavailable. AI chat will retry when the player sends a message.", LogLevel.Warn);
            return;
        }
        // Push the current config to a (possibly already-running) backend so manual
        // config.json edits apply without restarting the backend.
        await SyncLlmConfigToBackendAsync();
    }

    private void OnGameExiting(object? sender, EventArgs e)
    {
        backendClient?.Dispose();
        backendProcessManager?.Dispose();
    }

    private void TryAutoLoadTestSave()
    {
        if (!Config.TestMode || testAutoLoadDone)
            return;
        string? save = GetNextTestSave();
        if (string.IsNullOrWhiteSpace(save))
            return;
        if (Context.IsWorldReady || Game1.activeClickableMenu is not TitleMenu)
            return;
        testAutoLoadCounter++;
        if (testAutoLoadCounter < 180)
            return;
        testAutoLoadDone = true;
        try
        {
            Game1.activeClickableMenu = null;
            SaveGame.Load(save);
            Monitor.Log($"TestMode: auto-loading save '{save}'...", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Monitor.Log($"TestMode: auto-load failed: {ex.Message}", LogLevel.Error);
        }
    }

    private string? GetNextTestSave()
    {
        if (Config.TestSaveQueue.Length > 0)
        {
            return testSaveQueueIndex < Config.TestSaveQueue.Length
                ? Config.TestSaveQueue[testSaveQueueIndex]
                : null;
        }
        return string.IsNullOrWhiteSpace(Config.TestAutoLoadSave) ? null : Config.TestAutoLoadSave;
    }

    private void TryAdvanceTestSaveQueue()
    {
        if (!Config.TestMode || scenarioRunner is null || !scenarioRunner.IsDone)
            return;

        scenarioRunner.OnReturnedToTitle();
        if (Config.TestSaveQueue.Length == 0 || testSaveQueueIndex >= Config.TestSaveQueue.Length - 1)
        {
            Monitor.Log("TestMode: all queued saves completed.", LogLevel.Info);
            return;
        }

        testSaveQueueIndex++;
        Monitor.Log($"TestMode: advancing to next save '{Config.TestSaveQueue[testSaveQueueIndex]}'.", LogLevel.Info);
        Game1.ExitToTitle();
    }

    private static readonly (string Id, string Name, string Url, string Format)[] LlmProviders =
    {
        ("deepseek", "DeepSeek", "https://api.deepseek.com", "openai"),
        ("openai", "OpenAI", "https://api.openai.com/v1", "openai"),
        ("anthropic", "Anthropic Claude", "https://api.anthropic.com", "anthropic"),
        ("qwen", "通义千问 Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1", "openai"),
        ("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "openai"),
        ("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "openai"),
        ("custom", "自定义 / 本地 (Ollama 等)", "", "openai"),
    };

    private void RegisterGenericModConfigMenu()
    {
        IGenericModConfigMenuApi? api =
            Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null)
        {
            Monitor.Log(
                "Generic Mod Config Menu (GMCM) is not installed or its API is incompatible. " +
                "Install/update GMCM to configure in-game, or edit config.json directly.",
                LogLevel.Warn
            );
            return;
        }

        api.Register(
            ModManifest,
            reset: () => { },
            save: () =>
            {
                Helper.WriteConfig(Config);
                WriteLlmConfigFile();
                _ = SyncLlmConfigToBackendAsync();
            }
        );

        api.AddTextOption(
            ModManifest,
            () => Config.LlmProvider,
            value =>
            {
                Config.LlmProvider = value;
                int providerIndex = Array.FindIndex(LlmProviders, p => p.Id == value);
                if (providerIndex >= 0)
                    Config.LlmApiBase = LlmProviders[providerIndex].Url;
            },
            () => "LLM 服务商",
            () => "选择后自动带出官方地址，无需手填 URL",
            LlmProviders.Select(p => p.Id).ToArray(),
            value =>
            {
                int providerIndex = Array.FindIndex(LlmProviders, p => p.Id == value);
                return providerIndex >= 0 ? LlmProviders[providerIndex].Name : value;
            }
        );

        api.AddTextOption(
            ModManifest,
            () => Config.LlmApiKey,
            value => Config.LlmApiKey = value,
            () => "API Key",
            () => "填入你的 API Key（输入法请切到英文；若输入不全，直接用记事本改 config.json 里的 LlmApiKey）"
        );

        api.AddTextOption(
            ModManifest,
            () => Config.LlmModel,
            value => Config.LlmModel = value,
            () => "模型",
            () => "模型名，可手填（DeepSeek 用 deepseek-v4-flash）"
        );

        api.AddTextOption(
            ModManifest,
            () => Config.LlmApiBase,
            value => Config.LlmApiBase = value,
            () => "API 地址",
            () => "选了服务商会自动填；仅自定义/本地时需要手填"
        );

        api.AddBoolOption(
            ModManifest,
            () => Config.DebugLogging,
            value => Config.DebugLogging = value,
            () => "调试日志"
        );

        api.AddBoolOption(
            ModManifest,
            () => Config.EnableNativeImeCandidateWindow,
            value => Config.EnableNativeImeCandidateWindow = value,
            () => "显示中文输入法候选框",
            () => "在 Valewake 对话框附近显示 Windows 输入法的原生候选窗口"
        );

        Monitor.Log("Valewake settings registered in Generic Mod Config Menu.", LogLevel.Info);
    }

    private void WriteLlmConfigFile()
    {
        try
        {
            string dir = System.IO.Path.Combine(Helper.DirectoryPath, "Backend");
            System.IO.Directory.CreateDirectory(dir);
            int providerIndex = Array.FindIndex(LlmProviders, p => p.Id == Config.LlmProvider);
            if (providerIndex < 0)
                providerIndex = 0;
            string apiBase = string.IsNullOrWhiteSpace(Config.LlmApiBase)
                ? LlmProviders[providerIndex].Url
                : Config.LlmApiBase;
            var payload = new Dictionary<string, string>
            {
                ["llm_backend"] = LlmProviders[providerIndex].Format,
                ["llm_api_base"] = apiBase,
                ["llm_api_key"] = Config.LlmApiKey,
                ["llm_model"] = Config.LlmModel,
            };
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(dir, "llm_config.json"),
                System.Text.Json.JsonSerializer.Serialize(
                    payload,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to write LLM config file: {ex.Message}", LogLevel.Warn);
        }
    }

    private void LogLlmKeySummary()
    {
        string key = Config.LlmApiKey ?? "";
        string suffix = key.Length >= 4 ? key.Substring(key.Length - 4) : key;
        Monitor.Log(
            $"LLM config: provider={Config.LlmProvider}, model={Config.LlmModel}, api_key length={key.Length} (ends '...{suffix}').",
            LogLevel.Info
        );
    }

    private async Task SyncLlmConfigToBackendAsync()
    {
        if (backendClient is null || string.IsNullOrWhiteSpace(Config.LlmApiKey))
            return;
        try
        {
            await backendClient.SetLlmConfigAsync(new LlmConfigRequest
            {
                Provider = Config.LlmProvider,
                ApiBase = Config.LlmApiBase,
                ApiKey = Config.LlmApiKey,
                Model = Config.LlmModel,
                Backend = "",
            });
            Monitor.Log($"LLM config synced: {Config.LlmProvider} / {Config.LlmModel}.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to sync LLM config to backend: {ex.Message}", LogLevel.Warn);
        }
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        VanillaDialogueObserver.Reset();
        journalSession++;
        lastTurnId = "";
        memorySessionReady = RollbackCurrentSaveMemoryAsync("save_loaded_rollback");
        relationshipManager?.Load();
        actionJobManager?.Load();
        mineExpeditionManager?.Load();
        meetingManager?.Load();
        scenarioRunner?.OnSaveLoaded();
        Monitor.Log($"Save loaded for {Game1.player.Name} on {Game1.player.farmName.Value} Farm.", LogLevel.Info);
        LogSnapshot("Initial save snapshot");
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        memorySessionReady = CommitCurrentSaveMemoryAsync();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        scenarioRunner?.OnDayStarted();
        LogSnapshot("Day started");
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        VanillaDialogueObserver.Observe(Game1.activeClickableMenu as DialogueBox);
        TryAutoLoadTestSave();

        while (mainThreadActions.TryDequeue(out Action? action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to run queued agent UI action: {ex}", LogLevel.Error);
            }
        }

        actionJobManager?.Update();
        mineExpeditionManager?.Update();
        meetingManager?.Update();
        scenarioRunner?.OnUpdateTicked();
        TryAdvanceTestSaveQueue();

        if (pendingVanillaNpc is not null &&
            !pendingVanillaDialogueSeen &&
            Game1.ticks - pendingVanillaStartedTick > 30)
        {
            NPC npc = pendingVanillaNpc;
            ClearPendingVanillaDialogue();
            if (Game1.activeClickableMenu is null &&
                Context.IsPlayerFree &&
                CanStartAiDialogue(npc))
            {
                BeginChatSession(npc, "");
            }
        }
    }

    private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.DebugLogging)
            return;

        if (Game1.timeOfDay % 100 == 0 && e.IsMultipleOf(30))
            LogSnapshot("Periodic snapshot", LogLevel.Trace);
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (Context.IsWorldReady)
            actionJobManager?.Draw(e.SpriteBatch);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        VanillaDialogueObserver.Reset();
        journalSession++;
        scenarioRunner?.OnReturnedToTitle();
        testAutoLoadDone = false;
        testAutoLoadCounter = 0;
        string saveFolder = Constants.SaveFolderName ?? "";
        if (!string.IsNullOrWhiteSpace(saveFolder))
            _ = RollbackMemoryAsync($"{saveFolder}:", "save_rolled_back");
        EndChatSession();
        lastTurnId = "";
        ClearPendingVanillaDialogue();
        Monitor.Log("Returned to title screen.", LogLevel.Trace);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (Game1.activeClickableMenu is NpcChatInputMenu chatMenu && e.Button == SButton.Enter)
        {
            // Handle chat submission before Stardew/MonoGame sees Enter. On some
            // Windows fullscreen setups the first Enter after focusing a custom
            // text box leaks into the display-mode shortcut path.
            Helper.Input.Suppress(e.Button);
            chatMenu.SubmitFromKeyboard();
            return;
        }
        if (Context.IsWorldReady && e.Button == Config.ExpeditionTargetButton && mineExpeditionManager?.HasActiveExpedition == true)
        {
            SetExpeditionTarget(e.Cursor.GrabTile);
            Helper.Input.Suppress(e.Button);
            return;
        }
        if (!Config.EnableRightClickChat || !Context.IsWorldReady || !e.Button.IsActionButton())
            return;
        if (Game1.activeClickableMenu is not null || !Context.IsPlayerFree)
            return;

        NPC? npc = FindTargetNpcForAction(e.Cursor.GrabTile);
        if (npc is null || !CanStartAiDialogue(npc))
            return;

        if (!Config.PreserveVanillaFirstDialogue)
        {
            Helper.Input.Suppress(e.Button);
            BeginChatSession(npc, "");
            return;
        }

        pendingVanillaNpc = npc;
        pendingVanillaDialogueSeen = false;
        pendingVanillaStartedTick = Game1.ticks;
        pendingVanillaLine = "";
    }

    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        VanillaDialogueObserver.Observe(e.NewMenu as DialogueBox);
        if (pendingVanillaNpc is null)
            return;

        if (e.NewMenu is DialogueBox && IsCurrentSpeaker(pendingVanillaNpc))
        {
            pendingVanillaDialogueSeen = true;
            pendingVanillaLine = TryReadCurrentDialogueLine(pendingVanillaNpc);
            return;
        }

        if (pendingVanillaDialogueSeen && e.OldMenu is DialogueBox && e.NewMenu is null)
        {
            NPC npc = pendingVanillaNpc;
            string openingLine = pendingVanillaLine;
            ClearPendingVanillaDialogue();
            mainThreadActions.Enqueue(() => BeginChatSession(npc, openingLine));
        }
    }

    private void OnStateCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            Monitor.Log("Load a save before requesting an agent state snapshot.", LogLevel.Warn);
            return;
        }

        LogSnapshot("Console snapshot");
    }

    private void OnChatCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            Monitor.Log("Load a save before chatting with the AI NPC.", LogLevel.Warn);
            return;
        }

        string playerInput = string.Join(" ", args).Trim();
        if (string.IsNullOrWhiteSpace(playerInput))
        {
            Monitor.Log($"Usage: {Config.ChatCommandName} <message>", LogLevel.Info);
            return;
        }

        NPC? npc = FindNearbyTargetNpc();
        if (npc is null)
        {
            Monitor.Log(
                $"Move within {Config.NpcInteractionRadiusTiles} tiles of a social NPC before using {Config.ChatCommandName}.",
                LogLevel.Warn
            );
            return;
        }

        Game1.activeClickableMenu = new NpcThinkingMenu(npc);
        _ = SendChatToBackendAsync(playerInput, npc, Array.Empty<AgentConversationMessage>(), continueConversation: false);
    }

    private void OnMarkCommand(string command, string[] args)
    {
        string[] allowed =
        {
            "keep", "reject", "boundary", "memory_good", "memory_bad", "action_good", "action_bad"
        };
        if (args.Length == 0 || !allowed.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            Monitor.Log("Usage: agent_mark <keep|reject|boundary|memory_good|memory_bad|action_good|action_bad> [note]", LogLevel.Info);
            return;
        }
        if (string.IsNullOrWhiteSpace(lastTurnId))
        {
            Monitor.Log("No AI turn has been shown in this game session yet.", LogLevel.Warn);
            return;
        }

        string label = args[0].ToLowerInvariant();
        string note = string.Join(" ", args.Skip(1)).Trim();
        _ = MarkLatestTurnAsync(lastTurnId, label, note);
    }

    private void LogSnapshot(string label, LogLevel level = LogLevel.Info)
    {
        if (!Context.IsWorldReady)
            return;

        PerceptionSnapshot snapshot = PerceptionSnapshot.FromGame();
        Monitor.Log($"{label}: {snapshot.ToSummary()}", level);
    }

    private async Task SendChatToBackendAsync(
        string playerInput,
        NPC npc,
        IReadOnlyCollection<AgentConversationMessage> recentHistory,
        bool continueConversation)
    {
        if (MeetingRules.IsJoinRequest(playerInput) && meetingManager?.CanJoinTravel(npc) == true)
        {
            ShowJoinMeetingConfirmation(npc);
            return;
        }
        if (MeetingRules.IsEndRequest(playerInput) && meetingManager?.HasActiveMeeting(npc.Name) == true)
        {
            ShowEndMeetingConfirmation(npc, playerInput);
            return;
        }
        ulong journalSave = Game1.uniqueIDForThisGame;
        long journalPlayer = Game1.player.UniqueMultiplayerID;
        int journalDay = Game1.Date.TotalDays;
        int journalTime = Game1.timeOfDay;
        int requestJournalSession = journalSession;
        if (backendClient is null)
        {
            Monitor.Log("Backend client is not initialized.", LogLevel.Error);
            return;
        }

        if (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync())
        {
            mainThreadActions.Enqueue(() =>
            {
                NPC? currentNpc = FindNpcInCurrentLocation(npc.Name) ?? npc;
                ShowNpcDialogue(
                    currentNpc,
                    "Sorry, I can't collect my thoughts right now. Please check the SMAPI log.",
                    continueConversation && IsActiveChatNpc(currentNpc)
                        ? () => OpenChatInput(currentNpc)
                        : null
                );
            });
            return;
        }
        await memorySessionReady;

        NpcPerceptionSnapshot npcPerception = NpcPerceptionSnapshot.FromGame(npc);
        meetingManager?.MarkInteraction(npc.Name);
        AgentRelationshipContext agentRelationship =
            relationshipManager?.GetContext(npc.Name) ?? new AgentRelationshipContext();
        var gameState = new
        {
            source = "stardew_valley_smapi",
            schema_version = npcPerception.SchemaVersion,
            agent_name = Config.AgentName,
            npc = new
            {
                name = npc.Name,
                display_name = npc.displayName,
                age_group = npc.Age switch
                {
                    2 => "child",
                    1 => "teen",
                    _ => "adult"
                },
                location = npc.currentLocation?.NameOrUniqueName ?? "",
                tile_x = (int)npc.Tile.X,
                tile_y = (int)npc.Tile.Y
            },
            player = new
            {
                name = Game1.player.Name
            },
            npc_perception = npcPerception,
            npc_events = NpcInteractionJournal.ForNpc(Game1.player, npc.Name),
            npc_dialogue_events = VanillaDialogueJournal.ForNpc(Game1.player, npc.Name),
            knowledge_state = new { seen_event_ids = Game1.player.eventsSeen.ToArray() },
            agent_relationship = agentRelationship,
            execution_facts = (actionJobManager?.GetNpcFacts(npc.Name) ?? Array.Empty<object>())
                .Concat(mineExpeditionManager?.GetNpcFacts(npc.Name) ?? Array.Empty<object>())
                .Concat(meetingManager?.GetNpcFacts(npc.Name) ?? Array.Empty<object>()).ToArray(),
            action_rules = new
            {
                enabled = Config.EnableActionAgent,
                water_crops_enabled = Config.EnableWaterCropsAction,
                clear_weeds_enabled = Config.EnableClearWeedsAction,
                chop_trees_enabled = Config.EnableChopTreesAction,
                mine_expeditions_enabled = Config.EnableMineExpeditions,
                minimum_farm_hearts = Config.MinimumActionHearts,
                minimum_expedition_hearts = Config.MinimumExpeditionHearts,
                minimum_trust = Config.MinimumActionTrust,
                current_trust = agentRelationship.Trust,
                request_attempt_limit = Math.Max(1, Config.ActionRequestAttemptLimit),
                current_time = Game1.timeOfDay,
                meeting_minimum_lead_minutes = Math.Max(10, Config.MeetingMinimumLeadMinutes),
                farm_end_time = 2200,
                expedition_end_time = Config.ExpeditionEndTime,
                is_host = Context.IsMainPlayer,
                event_active = Game1.eventUp || Game1.currentLocation?.currentEvent is not null || Game1.isFestival(),
                npc_is_child = npc.Age == 2
            }
        };

        AgentChatRequest request = new()
        {
            PlayerInput = playerInput,
            GameState = gameState,
            SessionId = $"{Constants.SaveFolderName}:{NpcNameMap.Canonical(npc.Name)}",
            ConversationHistory = recentHistory
                .TakeLast(Math.Max(2, Config.MaxConversationHistoryMessages))
                .ToList(),
            Debug = Config.DebugLogging
        };

        try
        {
            AgentChatResponse response = await backendClient.SendChatAsync(request);
            Monitor.Log(
                $"AI chat response from {npc.Name}. Turn={response.TurnId}, Lore={response.RetrievedLore.Count}, " +
                $"Memory={response.RetrievedMemory.Count}, SavedMemory={response.SavedMemories.Count}, " +
                $"Relationship={response.RelationshipEffect.Valence}/{response.RelationshipEffect.Intensity}, " +
                $"Emotion={response.Emotion}",
                LogLevel.Info
            );

            mainThreadActions.Enqueue(() =>
            {
                if (!Context.IsWorldReady || journalSession != requestJournalSession || Game1.uniqueIDForThisGame != journalSave ||
                    Game1.player.UniqueMultiplayerID != journalPlayer)
                    return;
                lastTurnId = response.TurnId;
                NPC currentNpc = Game1.getCharacterFromName(npc.Name) ?? npc;
                RelationshipApplicationResult? relationshipResult = relationshipManager?.Apply(
                    currentNpc,
                    response.RelationshipEffect
                );
                if (relationshipResult is not null)
                {
                    _ = SendTraceEventSafeAsync("relationship_applied", new
                    {
                        turn_id = response.TurnId,
                        npc_name = currentNpc.Name,
                        was_applied = relationshipResult.WasApplied,
                        friendship_points = relationshipResult.FriendshipPoints,
                        reason = relationshipResult.Reason
                    });
                }
                ActionProposalDecision? actionDecision = null;
                bool isMineAction = response.ActionProposal is not null && ActionIds.IsMineAction(response.ActionProposal.Action);
                if (response.ActionProposal is not null && isMineAction && mineExpeditionManager is not null)
                {
                    actionDecision = mineExpeditionManager.Evaluate(
                        response.ActionProposal,
                        currentNpc,
                        playerInput,
                        relationshipManager?.GetContext(currentNpc.Name) ?? new AgentRelationshipContext(),
                        actionJobManager?.HasActiveJob(currentNpc.Name) == true
                    );
                    if (actionDecision.Allowed && meetingManager?.PrepareExpeditionHandoff(currentNpc.Name) is not null)
                        actionDecision = ActionProposalDecision.Allow(
                            LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh || Regex.IsMatch(playerInput, @"[\u4e00-\u9fff]")
                                ? $"结束当前约会或返程安排，改为让 {currentNpc.displayName} 陪你下矿吗？"
                                : $"End this meeting or return journey and have {currentNpc.displayName} join your mine expedition?", actionDecision.MaxTargets);
                    mineExpeditionManager.TraceProposalDecision(
                        response.ActionProposal,
                        currentNpc,
                        actionDecision,
                        response.TurnId
                    );
                }
                else if (response.ActionProposal is not null &&
                         response.ActionProposal.Action == ActionIds.ScheduleMeeting &&
                         meetingManager is not null)
                {
                    actionDecision = meetingManager.Evaluate(
                        response.ActionProposal,
                        currentNpc,
                        playerInput,
                        relationshipManager?.GetContext(currentNpc.Name) ?? new AgentRelationshipContext()
                    );
                    meetingManager.TraceDecision(response.ActionProposal, currentNpc, response.TurnId,
                        "proposal_validated", actionDecision.Allowed, actionDecision.Message);
                    Monitor.Log(
                        actionDecision.Allowed
                            ? $"Meeting proposal validated: {response.ActionProposal.Action}"
                            : $"Meeting proposal rejected: {actionDecision.Message}",
                        actionDecision.Allowed ? LogLevel.Info : LogLevel.Warn
                    );
                }
                else if (response.ActionProposal is not null && actionJobManager is not null)
                {
                    actionDecision = meetingManager?.HasScheduledMeeting(currentNpc.Name) == true ||
                        mineExpeditionManager?.IsNpcBusy(currentNpc.Name) == true
                        ? ActionProposalDecision.Reject("这个角色已有约会或同行安排，先结束当前安排吧。")
                        : actionJobManager.Evaluate(
                        response.ActionProposal,
                        currentNpc,
                        playerInput,
                        relationshipManager?.GetContext(currentNpc.Name) ?? new AgentRelationshipContext()
                    );
                    actionJobManager.TraceProposalDecision(
                        response.ActionProposal,
                        currentNpc,
                        actionDecision,
                        response.TurnId
                    );
                    Monitor.Log(
                        actionDecision.Allowed
                            ? $"Action proposal validated: {response.ActionProposal.Action}"
                            : $"Action proposal rejected: {actionDecision.Message}",
                        actionDecision.Allowed ? LogLevel.Info : LogLevel.Warn
                    );
                }

                string displayReply = response.Reply;
                if (isMineAction && actionDecision?.Allowed == false)
                {
                    displayReply = MeetingRules.MineRejectionReply(actionDecision.Message,
                        LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh || Regex.IsMatch(playerInput, @"[\u4e00-\u9fff]"));
                    _ = SendTraceEventSafeAsync("mine_reply_reconciled", new { turn_id = response.TurnId,
                        npc_name = currentNpc.Name, reason = actionDecision.Message, original_reply = response.Reply, displayed_reply = displayReply });
                }
                if (response.ActionProposal?.Action == ActionIds.ScheduleMeeting && actionDecision is not null && !actionDecision.Allowed)
                {
                    displayReply = MeetingRules.RejectionReply(actionDecision.Message,
                        LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh);
                    _ = SendTraceEventSafeAsync("meeting_reply_reconciled", new { turn_id = response.TurnId,
                        npc_name = currentNpc.Name, allowed = false, reason = actionDecision.Message,
                        original_reply = response.Reply, displayed_reply = displayReply, reconciliation_style = "in_character_replacement" });
                }
                if (Game1.Date.TotalDays == journalDay)
                    DailyDialogueJournal.Append(currentNpc.Name, journalTime, playerInput, displayReply);
                if (continueConversation && IsActiveChatNpc(currentNpc)) AddConversationMessage("assistant", displayReply);

                if (Config.TestMode && Config.TestAutoConfirm && scenarioRunner is not null && scenarioRunner.IsRunning)
                {
                    bool accepted = response.ActionProposal is not null && actionDecision?.Allowed == true;
                    if (accepted)
                        AcceptProposal(currentNpc, response.ActionProposal!, actionDecision!, response.TurnId, playerInput);
                    scenarioRunner.NotifyChatResult(
                        currentNpc.Name,
                        response.TurnId,
                        response.Reply,
                        response.ActionProposal?.Action ?? "",
                        accepted
                    );
                    return;
                }

                Action? afterDialogue = continueConversation && IsActiveChatNpc(currentNpc)
                    ? () => OpenChatInput(currentNpc)
                    : null;
                if (response.ActionProposal is not null && actionDecision?.Allowed == true)
                {
                    AgentActionProposal proposal = response.ActionProposal;
                    ActionProposalDecision decision = actionDecision;
                    afterDialogue = () => ShowActionConfirmation(
                        currentNpc,
                        proposal,
                        decision,
                        continueConversation,
                        response.TurnId,
                        playerInput
                    );
                }
                ShowNpcDialogue(
                    currentNpc,
                    ApplyPortraitEmotion(displayReply, actionDecision?.Allowed == false ? "neutral" : response.Emotion),
                    afterDialogue
                );
                if (Config.ShowRelationshipFeedback && relationshipResult?.WasApplied == true)
                    Game1.addHUDMessage(new HUDMessage(relationshipResult.Feedback));
                if (response.ActionProposal is not null &&
                    actionDecision is not null &&
                    !actionDecision.Allowed && response.ActionProposal.Action != ActionIds.ScheduleMeeting && !isMineAction)
                {
                    Game1.addHUDMessage(new HUDMessage($"Job not started: {actionDecision.Message}"));
                }
            });
        }
        catch (Exception ex)
        {
            Monitor.Log($"AI chat request failed: {ex.Message}", LogLevel.Error);
            mainThreadActions.Enqueue(() =>
            {
                NPC? currentNpc = FindNpcInCurrentLocation(npc.Name) ?? npc;
                ShowNpcDialogue(
                    currentNpc,
                    "Sorry, I can't collect my thoughts right now.",
                    continueConversation && IsActiveChatNpc(currentNpc)
                        ? () => OpenChatInput(currentNpc)
                        : null
                );
            });
        }
    }

    private async Task CommitCurrentSaveMemoryAsync()
    {
        string saveFolder = Constants.SaveFolderName ?? "";
        if (!string.IsNullOrWhiteSpace(saveFolder))
            await CommitMemoryAsync($"{saveFolder}:");
    }

    private async Task RollbackCurrentSaveMemoryAsync(string eventName)
    {
        string saveFolder = Constants.SaveFolderName ?? "";
        if (!string.IsNullOrWhiteSpace(saveFolder))
            await RollbackMemoryAsync($"{saveFolder}:", eventName);
    }

    private async Task CommitMemoryAsync(string sessionPrefix)
    {
        string saveId = Constants.SaveFolderName ?? "";
        int gameDay = Context.IsWorldReady ? Game1.Date.TotalDays : -1;
        int timeOfDay = Context.IsWorldReady ? Game1.timeOfDay : -1;
        try
        {
            if (backendClient is null ||
                (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync()))
            {
                return;
            }
            await backendClient.CommitMemoryAsync(sessionPrefix);
            await backendClient.SendTraceEventAsync("save_committed", new
            {
                session_prefix = sessionPrefix,
                save_id = saveId,
                game_day = gameDay,
                time_of_day = timeOfDay
            });
            Monitor.Log($"Committed AI memory for {sessionPrefix}", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not commit AI memory: {ex.Message}", LogLevel.Warn);
        }
    }

    private async Task RollbackMemoryAsync(string sessionPrefix, string eventName)
    {
        string saveId = Constants.SaveFolderName ?? "";
        int gameDay = Context.IsWorldReady ? Game1.Date.TotalDays : -1;
        int timeOfDay = Context.IsWorldReady ? Game1.timeOfDay : -1;
        try
        {
            if (backendClient is null ||
                (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync()))
            {
                return;
            }
            await backendClient.RollbackMemoryAsync(sessionPrefix);
            await backendClient.SendTraceEventAsync(eventName, new
            {
                session_prefix = sessionPrefix,
                save_id = saveId,
                game_day = gameDay,
                time_of_day = timeOfDay
            });
            Monitor.Log($"Rolled AI memory back to the last game save for {sessionPrefix}", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not roll back AI memory: {ex.Message}", LogLevel.Warn);
        }
    }

    private MineExpedition? TryAcceptMineProposal(NPC npc, AgentActionProposal proposal, string turnId, string input)
    {
        if (!Context.IsWorldReady || mineExpeditionManager is null) return null;
        ActionProposalDecision fresh = mineExpeditionManager.Evaluate(proposal, npc, input,
            relationshipManager?.GetContext(npc.Name) ?? new AgentRelationshipContext(), actionJobManager?.HasActiveJob(npc.Name) == true);
        if (!fresh.Allowed)
        {
            mineExpeditionManager.TraceProposalDecision(proposal, npc, fresh, turnId);
            Game1.addHUDMessage(new HUDMessage(MeetingRules.MineRejectionReply(fresh.Message,
                LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh || Regex.IsMatch(input, @"[\u4e00-\u9fff]"))));
            return null;
        }
        MeetingExpeditionHandoff? handoff = meetingManager?.PrepareExpeditionHandoff(npc.Name);
        MineExpedition expedition = mineExpeditionManager.Accept(proposal, npc, fresh, turnId, handoff);
        if (handoff is not null) meetingManager!.CompleteExpeditionHandoff(handoff, expedition.ExpeditionId);
        return expedition;
    }

    private void AcceptProposal(
        NPC npc,
        AgentActionProposal proposal,
        ActionProposalDecision decision,
        string sourceTurnId,
        string playerInput)
    {
        bool isMineAction = ActionIds.IsMineAction(proposal.Action);
        bool isMeeting = proposal.Action == ActionIds.ScheduleMeeting;
        if (!Context.IsWorldReady ||
            (isMineAction ? mineExpeditionManager is null
             : isMeeting ? meetingManager is null
             : actionJobManager is null))
            return;

        if (isMineAction)
        {
            mineExpeditionManager!.TraceConfirmation(proposal, npc, true, sourceTurnId);
            bool upgraded = mineExpeditionManager!.HasActiveExpedition;
            MineExpedition? expedition = TryAcceptMineProposal(npc, proposal, sourceTurnId, playerInput);
            if (expedition is null) return;
            Game1.addHUDMessage(new HUDMessage(
                upgraded
                    ? $"已为 {npc.displayName} 的同行任务追加能力：{proposal.Action}。"
                    : $"{npc.displayName} 已加入矿洞同行，将自动跟随、挖矿并防御。按 {Config.ExpeditionTargetButton} 可优先指定矿石。"
            ));
        }
        else if (isMeeting)
        {
            meetingManager!.TraceDecision(proposal, npc, sourceTurnId, "confirmation_accepted", true, "Player confirmed.");
            if (meetingManager!.Accept(proposal, npc, decision, sourceTurnId) is null) return;
            Game1.addHUDMessage(new HUDMessage($"已和 {npc.displayName} 约好见面，到时会通知你。"));
        }
        else
        {
            actionJobManager!.TraceConfirmation(proposal, npc, true, sourceTurnId);
            ActionJob job = actionJobManager!.Accept(proposal, npc, decision, sourceTurnId);
            Game1.addHUDMessage(new HUDMessage(FormatAcceptedJobMessage(npc, job)));
        }
        EndChatSession();
    }

    private void ShowActionConfirmation(
        NPC npc,
        AgentActionProposal proposal,
        ActionProposalDecision decision,
        bool continueConversation,
        string sourceTurnId,
        string playerInput)
    {
        bool isMineAction = ActionIds.IsMineAction(proposal.Action);
        bool isMeeting = proposal.Action == ActionIds.ScheduleMeeting;
        if (!Context.IsWorldReady ||
            (isMineAction ? mineExpeditionManager is null
             : isMeeting ? meetingManager is null
             : actionJobManager is null))
            return;

        int confirmationSession = journalSession;
        ulong confirmationSave = Game1.uniqueIDForThisGame;
        long confirmationPlayer = Game1.player.UniqueMultiplayerID;
        if (isMeeting) meetingManager!.TraceDecision(proposal, npc, sourceTurnId, "confirmation_shown", true, "Opening player confirmation.");
        Game1.currentLocation.createQuestionDialogue(
            decision.ConfirmationQuestion,
            Game1.currentLocation.createYesNoResponses(),
            (_, answer) =>
            {
                bool confirmed = string.Equals(answer, "Yes", StringComparison.OrdinalIgnoreCase);
                if (!Context.IsWorldReady || journalSession != confirmationSession || Game1.uniqueIDForThisGame != confirmationSave ||
                    Game1.player.UniqueMultiplayerID != confirmationPlayer) return;
                if (isMeeting)
                    meetingManager!.TraceDecision(proposal, npc, sourceTurnId,
                        confirmed ? "confirmation_accepted" : "confirmation_declined", confirmed, "Player confirmation.");
                if (!isMeeting)
                {
                    if (isMineAction)
                        mineExpeditionManager!.TraceConfirmation(proposal, npc, confirmed, sourceTurnId);
                    else
                        actionJobManager!.TraceConfirmation(proposal, npc, confirmed, sourceTurnId);
                }
                if (confirmed)
                {
                    if (isMineAction)
                    {
                        bool upgraded = mineExpeditionManager!.HasActiveExpedition;
                        MineExpedition? expedition = TryAcceptMineProposal(npc, proposal, sourceTurnId, playerInput);
                        if (expedition is null) return;
                        Game1.addHUDMessage(new HUDMessage(
                            upgraded
                                ? $"已为 {npc.displayName} 的同行任务追加能力：{proposal.Action}。"
                                : $"{npc.displayName} 已加入矿洞同行，将自动跟随、挖矿并防御。按 {Config.ExpeditionTargetButton} 可优先指定矿石。"
                        ));
                    }
                    else if (isMeeting)
                    {
                        if (meetingManager!.Accept(proposal, npc, decision, sourceTurnId) is null) return;
                        Game1.addHUDMessage(new HUDMessage(
                            $"已和 {npc.displayName} 约好见面，到时会通知你。"
                        ));
                    }
                    else
                    {
                        ActionJob job = actionJobManager!.Accept(proposal, npc, decision, sourceTurnId);
                        Game1.addHUDMessage(new HUDMessage(FormatAcceptedJobMessage(npc, job)));
                    }
                    EndChatSession();
                    return;
                }

                if (continueConversation && IsActiveChatNpc(npc))
                    OpenChatInput(npc);
            },
            npc
        );
    }

    private static string FormatAcceptedJobMessage(NPC npc, ActionJob job) =>
        job.MaxTargets > 0
            ? $"{npc.displayName} accepted the job ({job.MaxTargets} max)."
            : $"{npc.displayName} accepted the job and will handle every eligible target.";

    private void SetExpeditionTarget(Vector2 tile)
    {
        ExpeditionTargetResult result = mineExpeditionManager?.SetPriorityTarget(tile)
            ?? new ExpeditionTargetResult { Message = "矿洞同行控制器不可用。" };
        if (Context.IsWorldReady)
            Game1.addHUDMessage(new HUDMessage(result.Message));
        Monitor.Log(result.Message, result.Accepted ? LogLevel.Info : LogLevel.Warn);
    }

    private async Task MarkLatestTurnAsync(string turnId, string label, string note)
    {
        try
        {
            if (backendClient is null ||
                (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync()))
                return;
            await backendClient.MarkTurnAsync(turnId, label, note);
            Monitor.Log($"Marked {turnId} as {label}{(string.IsNullOrWhiteSpace(note) ? "" : $": {note}")}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not annotate AI turn: {ex.Message}", LogLevel.Warn);
        }
    }

    private async Task SendTraceEventSafeAsync(string eventName, object payload)
    {
        try
        {
            if (backendClient is null ||
                (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync()))
                return;
            await backendClient.SendTraceEventAsync(eventName, payload);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not append trace event '{eventName}': {ex.Message}", LogLevel.Warn);
        }
    }

    private void BeginChatSession(NPC npc, string vanillaOpeningLine)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null)
            return;
        activeChatNpc = npc;
        conversationHistory.Clear();
        if (!string.IsNullOrWhiteSpace(vanillaOpeningLine))
            AddConversationMessage("assistant", vanillaOpeningLine);
        npc.faceTowardFarmerForPeriod(3000, 4, faceAway: false, Game1.player);
        OpenChatInput(npc);
    }

    private void OpenChatInput(NPC npc, string draft = "")
    {
        if (!Context.IsWorldReady || !IsActiveChatNpc(npc))
        {
            EndChatSession();
            return;
        }
        Game1.activeClickableMenu = new NpcChatInputMenu(
            npc,
            playerInput => SubmitContinuousChat(npc, playerInput),
            EndChatSession,
            Config.EnableNativeImeCandidateWindow,
            text => Game1.activeClickableMenu = new NpcDialogueHistoryMenu(npc, () => OpenChatInput(npc, text)),
            draft,
            meetingManager?.HasActiveMeeting(npc.Name) == true ? () => ShowEndMeetingConfirmation(npc, "结束约会") : null,
            Config.MaxChatInputCharacters,
            meetingManager?.CanJoinTravel(npc) == true ? () => ShowJoinMeetingConfirmation(npc) : null
        );
    }

    private void ShowJoinMeetingConfirmation(NPC npc)
    {
        Game1.currentLocation.createQuestionDialogue("现在开始这次约会，一起走吗？",
            new[] { new Response("join", "一起过去"), new Response("wait", "按原计划见面") },
            (_, answer) =>
            {
                if (answer == "join" && meetingManager?.JoinTravel(npc) == true)
                {
                    const string reply = "好，那我们一起走吧。";
                    DailyDialogueJournal.Append(npc.Name, Game1.timeOfDay, "一起过去吧", reply);
                    AddConversationMessage("assistant", reply);
                    ShowNpcDialogue(npc, reply, () => OpenChatInput(npc));
                }
                else OpenChatInput(npc);
            });
    }

    private void ShowEndMeetingConfirmation(NPC npc, string input)
    {
        Game1.currentLocation.createQuestionDialogue("结束这次约会，让对方回去吗？",
            new[] { new Response("end", "结束约会"), new Response("stay", "继续约会") },
            (_, answer) =>
            {
                if (answer == "end" && meetingManager?.EndForNpc(npc.Name, input) == true)
                {
                    DailyDialogueJournal.Append(npc.Name, Game1.timeOfDay, input, "好，今天就到这里吧，我先回去了。");
                    ShowNpcDialogue(npc, "好，今天就到这里吧，我先回去了。", EndChatSession);
                }
                else OpenChatInput(npc);
            });
    }

    private void SubmitContinuousChat(NPC npc, string playerInput)
    {
        if (!IsActiveChatNpc(npc))
            return;
        List<AgentConversationMessage> historyBeforeInput = conversationHistory.ToList();
        AddConversationMessage("user", playerInput);
        Game1.activeClickableMenu = new NpcThinkingMenu(npc);
        _ = SendChatToBackendAsync(playerInput, npc, historyBeforeInput, continueConversation: true);
    }

    private void AddConversationMessage(string role, string content)
    {
        conversationHistory.Add(new AgentConversationMessage { Role = role, Content = content });
        int limit = Math.Max(2, Config.MaxConversationHistoryMessages);
        if (conversationHistory.Count > limit)
            conversationHistory.RemoveRange(0, conversationHistory.Count - limit);
    }

    private void EndChatSession()
    {
        if (activeChatNpc is not null && Context.IsWorldReady) meetingManager?.ResumeAfterDialogue(activeChatNpc.Name);
        activeChatNpc = null;
        conversationHistory.Clear();
    }

    private bool IsActiveChatNpc(NPC npc) =>
        activeChatNpc is not null && string.Equals(activeChatNpc.Name, npc.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentSpeaker(NPC npc) =>
        Game1.currentSpeaker is not null && string.Equals(Game1.currentSpeaker.Name, npc.Name, StringComparison.OrdinalIgnoreCase);

    private bool CanStartAiDialogue(NPC npc)
    {
        if (Game1.eventUp || Game1.currentLocation?.currentEvent is not null || Game1.isFestival())
            return false;
        if (Game1.player.ActiveObject is not null)
            return false;
        return !npc.IsInvisible && !npc.isSleeping.Value && npc.CanSocialize;
    }

    private NPC? FindTargetNpcForAction(Vector2 cursorTile)
    {
        if (Game1.currentLocation is null)
            return null;

        NPC? clicked = Game1.currentLocation.characters
            .Where(IsConfiguredTarget)
            .Where(npc => TileDistance(npc.Tile, cursorTile) <= 1)
            .OrderBy(npc => TileDistance(npc.Tile, cursorTile))
            .FirstOrDefault();
        if (clicked is not null)
            return clicked;

        Vector2 facingTile = Game1.player.Tile + Game1.player.FacingDirection switch
        {
            0 => new Vector2(0, -1),
            1 => new Vector2(1, 0),
            2 => new Vector2(0, 1),
            3 => new Vector2(-1, 0),
            _ => Vector2.Zero
        };
        return Game1.currentLocation.characters
            .Where(IsConfiguredTarget)
            .FirstOrDefault(npc => TileDistance(npc.Tile, facingTile) <= 1);
    }

    private bool IsConfiguredTarget(NPC npc) =>
        Config.EnableAllSocialNpcs
            ? npc.CanSocialize && !Config.ExcludedNpcNames.Contains(
                npc.Name,
                StringComparer.OrdinalIgnoreCase
            )
            : string.Equals(npc.Name, Config.TargetNpcName, StringComparison.OrdinalIgnoreCase);

    private static int TileDistance(Vector2 left, Vector2 right) =>
        Math.Max(Math.Abs((int)left.X - (int)right.X), Math.Abs((int)left.Y - (int)right.Y));

    private static string TryReadCurrentDialogueLine(NPC npc)
    {
        try
        {
            if (npc.CurrentDialogue.Count == 0)
                return "";
            return string.Join(" ", npc.CurrentDialogue.Peek().dialogues.Select(line => line.Text));
        }
        catch
        {
            return "";
        }
    }

    private void ClearPendingVanillaDialogue()
    {
        pendingVanillaNpc = null;
        pendingVanillaDialogueSeen = false;
        pendingVanillaStartedTick = 0;
        pendingVanillaLine = "";
    }

    private NPC? FindNearbyTargetNpc()
    {
        return Game1.currentLocation?.characters
            .Where(IsConfiguredTarget)
            .Where(npc => TileDistance(npc.Tile, Game1.player.Tile) <= Config.NpcInteractionRadiusTiles)
            .OrderBy(npc => TileDistance(npc.Tile, Game1.player.Tile))
            .FirstOrDefault();
    }

    private static NPC? FindNpcInCurrentLocation(string npcName)
    {
        return Game1.currentLocation?.characters
            .FirstOrDefault(npc => string.Equals(npc.Name, npcName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ShowNpcDialogue(NPC npc, string text, Action? onFinish = null)
    {
        npc.faceTowardFarmerForPeriod(3000, 4, faceAway: false, Game1.player);
        Dialogue dialogue = new(npc, null, text);
        if (onFinish is not null)
            dialogue.onFinish = onFinish;
        DialogueBox box = new(dialogue);
        VanillaDialogueObserver.MarkGenerated(box);
        Game1.activeClickableMenu = box;
    }

    private static string ApplyPortraitEmotion(string reply, string emotion)
    {
        string cleanReply = Regex.Replace(reply ?? "", @"#?\$(?:h|s|a|l|0)\b", "", RegexOptions.IgnoreCase).Trim();
        string portraitToken = (emotion ?? "").Trim().ToLowerInvariant() switch
        {
            "happy" => "$h",
            "sad" => "$s",
            "angry" => "$a",
            "affectionate" => "$l",
            _ => ""
        };
        return string.IsNullOrEmpty(portraitToken) ? cleanReply : $"{cleanReply} {portraitToken}";
    }
}
