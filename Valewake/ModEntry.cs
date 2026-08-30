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
    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly List<AgentConversationMessage> conversationHistory = new();
    private Task memorySessionReady = Task.CompletedTask;
    private NPC? activeChatNpc;
    private NPC? pendingVanillaNpc;
    private bool pendingVanillaDialogueSeen;
    private int pendingVanillaStartedTick;
    private string pendingVanillaLine = "";
    private string lastTurnId = "";

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
            return;

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

        api.AddDropdownOption(
            ModManifest,
            () => Array.FindIndex(LlmProviders, p => p.Id == Config.LlmProvider),
            value =>
            {
                Config.LlmProvider = LlmProviders[value].Id;
                Config.LlmApiBase = LlmProviders[value].Url;
            },
            () => LlmProviders.Select(p => p.Name).ToArray(),
            () => "LLM 服务商",
            () => "选择后自动带出官方地址，无需手填 URL"
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
        lastTurnId = "";
        memorySessionReady = RollbackCurrentSaveMemoryAsync("save_loaded_rollback");
        relationshipManager?.Load();
        actionJobManager?.Load();
        mineExpeditionManager?.Load();
        Monitor.Log($"Save loaded for {Game1.player.Name} on {Game1.player.farmName.Value} Farm.", LogLevel.Info);
        LogSnapshot("Initial save snapshot");
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        memorySessionReady = CommitCurrentSaveMemoryAsync();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        LogSnapshot("Day started");
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
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
            agent_relationship = agentRelationship,
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
            SessionId = $"{Constants.SaveFolderName}:{npc.Name}",
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
                if (!Context.IsWorldReady)
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
                if (continueConversation && IsActiveChatNpc(currentNpc))
                    AddConversationMessage("assistant", response.Reply);

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
                    mineExpeditionManager.TraceProposalDecision(
                        response.ActionProposal,
                        currentNpc,
                        actionDecision,
                        response.TurnId
                    );
                }
                else if (response.ActionProposal is not null && actionJobManager is not null)
                {
                    actionDecision = actionJobManager.Evaluate(
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
                        response.TurnId
                    );
                }
                ShowNpcDialogue(
                    currentNpc,
                    ApplyPortraitEmotion(response.Reply, response.Emotion),
                    afterDialogue
                );
                if (Config.ShowRelationshipFeedback && relationshipResult?.WasApplied == true)
                    Game1.addHUDMessage(new HUDMessage(relationshipResult.Feedback));
                if (response.ActionProposal is not null &&
                    actionDecision is not null &&
                    !actionDecision.Allowed)
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

    private void ShowActionConfirmation(
        NPC npc,
        AgentActionProposal proposal,
        ActionProposalDecision decision,
        bool continueConversation,
        string sourceTurnId)
    {
        bool isMineAction = ActionIds.IsMineAction(proposal.Action);
        if (!Context.IsWorldReady || (isMineAction ? mineExpeditionManager is null : actionJobManager is null))
            return;

        Game1.currentLocation.createQuestionDialogue(
            decision.ConfirmationQuestion,
            Game1.currentLocation.createYesNoResponses(),
            (_, answer) =>
            {
                bool confirmed = string.Equals(answer, "Yes", StringComparison.OrdinalIgnoreCase);
                if (isMineAction)
                    mineExpeditionManager!.TraceConfirmation(proposal, npc, confirmed, sourceTurnId);
                else
                    actionJobManager!.TraceConfirmation(proposal, npc, confirmed, sourceTurnId);
                if (confirmed)
                {
                    if (isMineAction)
                    {
                        bool upgraded = mineExpeditionManager!.HasActiveExpedition;
                        MineExpedition expedition = mineExpeditionManager!.Accept(proposal, npc, decision, sourceTurnId);
                        Game1.addHUDMessage(new HUDMessage(
                            upgraded
                                ? $"已为 {npc.displayName} 的同行任务追加能力：{proposal.Action}。"
                                : $"{npc.displayName} 已加入矿洞同行，将自动跟随、挖矿并防御。按 {Config.ExpeditionTargetButton} 可优先指定矿石。"
                        ));
                    }
                    else
                    {
                        ActionJob job = actionJobManager!.Accept(proposal, npc, decision, sourceTurnId);
                        Game1.addHUDMessage(new HUDMessage(
                            $"{npc.displayName} accepted the job ({job.MaxTargets} max)."
                        ));
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

    private void OpenChatInput(NPC npc)
    {
        if (!Context.IsWorldReady || !IsActiveChatNpc(npc))
        {
            EndChatSession();
            return;
        }
        Game1.activeClickableMenu = new NpcChatInputMenu(
            npc,
            playerInput => SubmitContinuousChat(npc, playerInput),
            EndChatSession
        );
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
        Game1.activeClickableMenu = new DialogueBox(dialogue);
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
