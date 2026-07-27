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
    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly List<AgentConversationMessage> conversationHistory = new();
    private Task memorySessionReady = Task.CompletedTask;
    private NPC? activeChatNpc;
    private NPC? pendingVanillaNpc;
    private bool pendingVanillaDialogueSeen;
    private int pendingVanillaStartedTick;
    private string pendingVanillaLine = "";

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

        Monitor.Log(
            "Valewake loaded. Use '" + Config.StateCommandName +
            "' for state or '" + Config.ChatCommandName +
            " <message>' near a social NPC. Right-click any supported NPC for continuous dialogue.",
            LogLevel.Info
        );
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Monitor.Log($"Agent backend configured at: {Config.BackendUrl}", LogLevel.Info);
        _ = EnsureBackendReadyAsync();
    }

    private async Task EnsureBackendReadyAsync()
    {
        if (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync())
            Monitor.Log("Agent backend is unavailable. AI chat will retry when the player sends a message.", LogLevel.Warn);
    }

    private void OnGameExiting(object? sender, EventArgs e)
    {
        backendClient?.Dispose();
        backendProcessManager?.Dispose();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        memorySessionReady = RollbackCurrentSaveMemoryAsync();
        relationshipManager?.Load();
        actionJobManager?.Load();
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
            _ = RollbackMemoryAsync($"{saveFolder}:");
        EndChatSession();
        ClearPendingVanillaDialogue();
        Monitor.Log("Returned to title screen.", LogLevel.Trace);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
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
            agent_relationship = relationshipManager?.GetContext(npc.Name) ?? new AgentRelationshipContext()
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
                NPC currentNpc = Game1.getCharacterFromName(npc.Name) ?? npc;
                RelationshipApplicationResult? relationshipResult = relationshipManager?.Apply(
                    currentNpc,
                    response.RelationshipEffect
                );
                if (continueConversation && IsActiveChatNpc(currentNpc))
                    AddConversationMessage("assistant", response.Reply);

                ActionProposalDecision? actionDecision = null;
                if (response.ActionProposal is not null && actionJobManager is not null)
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
                        actionDecision
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
                        continueConversation
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

    private async Task RollbackCurrentSaveMemoryAsync()
    {
        string saveFolder = Constants.SaveFolderName ?? "";
        if (!string.IsNullOrWhiteSpace(saveFolder))
            await RollbackMemoryAsync($"{saveFolder}:");
    }

    private async Task CommitMemoryAsync(string sessionPrefix)
    {
        try
        {
            if (backendClient is null ||
                (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync()))
            {
                return;
            }
            await backendClient.CommitMemoryAsync(sessionPrefix);
            Monitor.Log($"Committed AI memory for {sessionPrefix}", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not commit AI memory: {ex.Message}", LogLevel.Warn);
        }
    }

    private async Task RollbackMemoryAsync(string sessionPrefix)
    {
        try
        {
            if (backendClient is null ||
                (backendProcessManager is not null && !await backendProcessManager.EnsureReadyAsync()))
            {
                return;
            }
            await backendClient.RollbackMemoryAsync(sessionPrefix);
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
        bool continueConversation)
    {
        if (!Context.IsWorldReady || actionJobManager is null)
            return;

        Game1.currentLocation.createQuestionDialogue(
            decision.ConfirmationQuestion,
            Game1.currentLocation.createYesNoResponses(),
            (_, answer) =>
            {
                bool confirmed = string.Equals(answer, "Yes", StringComparison.OrdinalIgnoreCase);
                actionJobManager.TraceConfirmation(proposal, npc, confirmed);
                if (confirmed)
                {
                    ActionJob job = actionJobManager.Accept(proposal, npc, decision);
                    Game1.addHUDMessage(new HUDMessage(
                        $"{npc.displayName} accepted the job ({job.MaxTargets} max)."
                    ));
                    EndChatSession();
                    return;
                }

                if (continueConversation && IsActiveChatNpc(npc))
                    OpenChatInput(npc);
            },
            npc
        );
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
