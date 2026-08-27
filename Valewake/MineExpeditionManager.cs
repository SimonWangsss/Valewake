using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using xTile.Dimensions;

namespace Valewake;

/// <summary>Runs bounded mine companionship locally; the LLM only proposes a high-level mode.</summary>
public sealed class MineExpeditionManager
{
    private const string SaveDataKey = "valewake-mine-expeditions-v1";
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly string tracePath;
    private MineExpeditionSaveData data = new();

    public MineExpeditionManager(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        tracePath = Path.Combine(helper.DirectoryPath, "data", "traces", "expedition_trace.jsonl");
    }

    public bool HasActiveExpedition => Active is not null;

    public void Load()
    {
        data = helper.Data.ReadSaveData<MineExpeditionSaveData>(SaveDataKey) ?? new MineExpeditionSaveData();
        foreach (MineExpedition expedition in data.Expeditions.Where(item => !MineExpeditionStates.IsTerminal(item.State)))
        {
            expedition.State = MineExpeditionStates.Failed;
            expedition.LastMessage = "The game was reloaded before the expedition ended.";
            RestoreNpc(expedition);
            Trace(expedition, "recovered_after_reload");
        }
        Save();
    }

    public ActionProposalDecision Evaluate(
        AgentActionProposal proposal,
        NPC npc,
        string playerInput,
        AgentRelationshipContext relationship,
        bool hasFarmJob)
    {
        if (!config.EnableActionAgent || !config.EnableMineExpeditions)
            return ActionProposalDecision.Reject("Mine expeditions are disabled.");
        if (!Context.IsMainPlayer)
            return ActionProposalDecision.Reject("Only the multiplayer host can authorize an expedition.");
        if (Game1.eventUp || Game1.currentLocation?.currentEvent is not null || Game1.isFestival())
            return ActionProposalDecision.Reject("An expedition cannot start during an event or festival.");
        if (npc.Age == 2)
            return ActionProposalDecision.Reject("Child NPCs cannot join mine expeditions.");
        if (!string.Equals(proposal.Disposition, "accept", StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject($"{npc.displayName} did not accept the expedition.");
        if (proposal.Confidence < config.MinimumActionConfidence)
            return ActionProposalDecision.Reject("The proposal was not confident enough.");
        if (string.IsNullOrWhiteSpace(proposal.Evidence) ||
            !playerInput.Contains(proposal.Evidence, StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject("The proposal was not grounded in the player's request.");
        if (Game1.timeOfDay >= config.ExpeditionEndTime)
            return ActionProposalDecision.Reject("It is too late to begin a mine expedition.");
        MineExpedition? active = Active;
        if (hasFarmJob)
            return ActionProposalDecision.Reject("That NPC is already reserved by an active farm job.");
        if (active is not null && !active.NpcName.Equals(npc.Name, StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject($"{active.NpcName} is already in the active expedition.");
        if (active is not null && !AddsCapability(active, proposal.Action))
            return ActionProposalDecision.Reject("That capability is already active in this expedition.");

        int hearts = Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship)
            ? friendship.Points / 250
            : 0;
        if (hearts < config.MinimumExpeditionHearts)
            return ActionProposalDecision.Reject($"This expedition requires at least {config.MinimumExpeditionHearts} hearts.");
        if (relationship.Trust < config.MinimumActionTrust)
            return ActionProposalDecision.Reject($"This expedition requires at least {config.MinimumActionTrust} Valewake trust.");
        if (proposal.Action == ActionIds.MineTarget && Game1.currentLocation is not MineShaft)
            return ActionProposalDecision.Reject("Point at a mine rock while inside the Mines.");

        int maximum = proposal.Action == ActionIds.MineTarget
            ? 1
            : Math.Min(GetRequestedMaximum(proposal), config.ExpeditionMaxMineTargets);
        string description = proposal.Action switch
        {
            ActionIds.JoinMineExpedition => "follow you through the mines, defend you, and mine nearby eligible rocks",
            ActionIds.DefendPlayer => "follow you and defend against nearby monsters",
            ActionIds.MineTarget => "mine the rock under your cursor",
            ActionIds.MineNearby => $"mine up to {maximum} nearby allowed rocks",
            _ => $"join a bounded expedition and mine up to {maximum} allowed rocks"
        };
        string prefix = active is null ? $"Let {npc.displayName}" : $"Ask {npc.displayName} to also";
        return ActionProposalDecision.Allow($"{prefix} {description}?", maximum);
    }

    public MineExpedition Accept(
        AgentActionProposal proposal,
        NPC npc,
        ActionProposalDecision decision,
        string sourceTurnId)
    {
        MineExpedition? active = Active;
        if (active is not null && active.NpcName.Equals(npc.Name, StringComparison.OrdinalIgnoreCase))
        {
            Upgrade(active, proposal, decision, sourceTurnId);
            return active;
        }

        bool defenseEnabled = proposal.Action is ActionIds.DefendPlayer or ActionIds.MineNearby or ActionIds.MineExpedition ||
                              proposal.Action == ActionIds.JoinMineExpedition && config.ExpeditionAutoDefendOnJoin;
        bool miningEnabled = proposal.Action is ActionIds.MineTarget or ActionIds.MineNearby or ActionIds.MineExpedition ||
                             proposal.Action == ActionIds.JoinMineExpedition && config.ExpeditionAutoMineOnJoin;
        MineExpedition expedition = new()
        {
            ExpeditionId = $"exp_{Guid.NewGuid():N}"[..16],
            SourceTurnId = sourceTurnId,
            ProposalId = proposal.ProposalId,
            SaveId = Constants.SaveFolderName ?? "",
            NpcName = npc.Name,
            Action = proposal.Action,
            DefenseEnabled = defenseEnabled,
            MiningEnabled = miningEnabled,
            MiningMode = proposal.Action == ActionIds.MineTarget
                ? "target"
                : miningEnabled ? "nearby" : "none",
            EndAfterMining = proposal.Action is ActionIds.MineTarget or ActionIds.MineNearby,
            State = MineExpeditionStates.Joining,
            ResourcePriority = GetStringParameter(proposal, "resource_priority", "any"),
            MaxTargets = miningEnabled ? decision.MaxTargets : 0,
            CreatedDay = Game1.Date.TotalDays,
            CreatedTime = Game1.timeOfDay,
            RuntimePlayerLocation = Game1.currentLocation?.NameOrUniqueName ?? "",
            RuntimeLocationChangedTick = Game1.ticks,
            RuntimeLastProgressTick = Game1.ticks,
            RuntimePriorityTile = proposal.Action == ActionIds.MineTarget
                ? ToActionTile(helper.Input.GetCursorPosition().GrabTile)
                : null,
            ReturnContext = new ActionReturnContext
            {
                LocationName = npc.currentLocation?.NameOrUniqueName ?? "",
                TileX = (int)npc.Tile.X,
                TileY = (int)npc.Tile.Y,
                FacingDirection = npc.FacingDirection,
                FollowSchedule = npc.followSchedule
            },
            OriginalAddedSpeed = npc.addedSpeed,
            OriginalSpeed = npc.Speed,
            LastCommandTurnId = sourceTurnId,
            LastCommandProposalId = proposal.ProposalId
        };
        ReserveNpc(npc, expedition);
        data.Expeditions.Add(expedition);
        Save();
        Trace(expedition, "accepted");
        return expedition;
    }

    private void Upgrade(
        MineExpedition expedition,
        AgentActionProposal proposal,
        ActionProposalDecision decision,
        string sourceTurnId)
    {
        expedition.LastCommandTurnId = sourceTurnId;
        expedition.LastCommandProposalId = proposal.ProposalId;
        expedition.EndAfterMining = false;
        switch (proposal.Action)
        {
            case ActionIds.DefendPlayer:
                expedition.DefenseEnabled = true;
                break;
            case ActionIds.MineTarget:
                expedition.MiningEnabled = true;
                expedition.MiningMode = "target";
                expedition.MaxTargets = Math.Min(
                    config.ExpeditionMaxMineTargets,
                    Math.Max(expedition.MaxTargets, expedition.CompletedTargets + 1)
                );
                TryAssignCursorTarget(expedition);
                break;
            case ActionIds.MineNearby:
                expedition.MiningEnabled = true;
                expedition.MiningMode = "nearby";
                expedition.MaxTargets = Math.Min(
                    config.ExpeditionMaxMineTargets,
                    Math.Max(expedition.MaxTargets, expedition.CompletedTargets + decision.MaxTargets)
                );
                break;
            case ActionIds.MineExpedition:
                expedition.DefenseEnabled = true;
                expedition.MiningEnabled = true;
                expedition.MiningMode = "nearby";
                expedition.ResourcePriority = GetStringParameter(proposal, "resource_priority", expedition.ResourcePriority);
                expedition.MaxTargets = Math.Min(
                    config.ExpeditionMaxMineTargets,
                    Math.Max(expedition.MaxTargets, expedition.CompletedTargets + decision.MaxTargets)
                );
                break;
        }
        expedition.Action = CanonicalAction(expedition, proposal.Action);
        Save();
        Trace(expedition, "capability_upgraded");
    }

    public void TraceProposalDecision(AgentActionProposal proposal, NPC npc, ActionProposalDecision decision, string turnId)
    {
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = "proposal_validated",
            turn_id = turnId,
            proposal_id = proposal.ProposalId,
            npc_name = npc.Name,
            action = proposal.Action,
            allowed = decision.Allowed,
            message = decision.Message,
            max_targets = decision.MaxTargets
        });
    }

    public void TraceConfirmation(AgentActionProposal proposal, NPC npc, bool confirmed, string turnId)
    {
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = confirmed ? "confirmation_accepted" : "confirmation_declined",
            turn_id = turnId,
            proposal_id = proposal.ProposalId,
            npc_name = npc.Name,
            action = proposal.Action,
            confirmed
        });
    }

    public void Update()
    {
        MineExpedition? expedition = Active;
        if (!Context.IsWorldReady || !Context.IsMainPlayer || expedition is null)
            return;
        NPC? npc = Game1.getCharacterFromName(expedition.NpcName);
        if (npc is null || Game1.currentLocation is null)
        {
            Fail(expedition, "NPC or player location became unavailable.");
            return;
        }
        MaintainReservation(npc, expedition);
        if (Game1.timeOfDay >= config.ExpeditionEndTime && expedition.State != MineExpeditionStates.Returning)
        {
            BeginReturn(expedition, "It is getting late; the expedition is ending.");
            return;
        }
        if (expedition.State == MineExpeditionStates.Returning)
        {
            UpdateReturning(expedition, npc);
            return;
        }
        if (Game1.ticks < expedition.RuntimeNextDecisionTick)
            return;
        expedition.RuntimeNextDecisionTick = Game1.ticks + 12;

        string playerLocation = Game1.currentLocation.NameOrUniqueName;
        if (!string.Equals(expedition.RuntimePlayerLocation, playerLocation, StringComparison.Ordinal))
        {
            expedition.RuntimePreviousPlayerLocation = expedition.RuntimePlayerLocation;
            expedition.RuntimePlayerLocation = playerLocation;
            expedition.RuntimeLocationChangedTick = Game1.ticks;
            expedition.RuntimeSkippedMineTiles.Clear();
            ResetTaskNavigation(expedition);
            Trace(expedition, "player_changed_location");
        }
        if (npc.currentLocation != Game1.currentLocation)
        {
            if (Game1.ticks - expedition.RuntimeLocationChangedTick < config.ExpeditionWarpDelayTicks)
                return;
            WarpAtArrival(npc, expedition.RuntimePreviousPlayerLocation);
            expedition.State = MineExpeditionStates.Following;
            Trace(expedition, "followed_across_location");
            return;
        }

        Monster? threat = CanDefend(expedition)
            ? FindThreat(Game1.currentLocation, Game1.player.Tile, config.ExpeditionDefenseRadiusTiles)
            : null;
        if (threat is not null)
        {
            UpdateCombat(expedition, npc, threat);
            return;
        }
        if (CanMine(expedition) && Game1.currentLocation is MineShaft)
        {
            if (UpdateMining(expedition, npc))
                return;
        }
        ResetTaskNavigation(expedition);
        FollowPlayer(expedition, npc);
    }

    public ExpeditionTargetResult SetPriorityTarget(Vector2 cursorTile)
    {
        MineExpedition? expedition = Active;
        if (expedition is null)
            return new ExpeditionTargetResult { Message = "当前没有同行中的 NPC。" };
        if (Game1.currentLocation is not MineShaft)
            return new ExpeditionTargetResult { Message = "请进入矿洞楼层后再指定矿石。" };
        KeyValuePair<Vector2, StardewValley.Object>? selected = FindMineNodeNearCursor(
            Game1.currentLocation,
            cursorTile,
            config.ExpeditionTargetSnapRadiusTiles
        );
        if (selected is null)
        {
            TraceTargetSelection(expedition, cursorTile, null, "no_allowed_node_near_cursor");
            return new ExpeditionTargetResult { Message = "鼠标下不是可开采的石头或矿石节点。" };
        }
        if (expedition.CompletedTargets >= config.ExpeditionMaxMineTargets)
            return new ExpeditionTargetResult { Message = $"本次探险已达到 {config.ExpeditionMaxMineTargets} 个开采目标的上限。" };
        Vector2 selectedTile = selected.Value.Key;
        StardewValley.Object obj = selected.Value.Value;
        expedition.MiningEnabled = true;
        if (expedition.MiningMode == "none")
            expedition.MiningMode = "target";
        expedition.EndAfterMining = false;
        expedition.Action = expedition.DefenseEnabled ? ActionIds.MineExpedition : ActionIds.MineTarget;
        expedition.MaxTargets = Math.Max(expedition.MaxTargets, expedition.CompletedTargets + 1);
        expedition.RuntimePriorityTile = ToActionTile(selectedTile);
        expedition.RuntimeMiningTile = null;
        ResetTaskNavigation(expedition);
        Save();
        TraceTargetSelection(expedition, cursorTile, selectedTile, "accepted");
        Trace(expedition, "priority_target_set");
        return new ExpeditionTargetResult
        {
            Accepted = true,
            Message = $"已指定矿石：{obj.DisplayName} [{(int)selectedTile.X},{(int)selectedTile.Y}]（{expedition.CompletedTargets + 1}/{expedition.MaxTargets}）。"
        };
    }

    public void BeginReturn(string reason)
    {
        MineExpedition? expedition = Active;
        if (expedition is not null)
            BeginReturn(expedition, reason);
    }

    public string GetSummary()
    {
        MineExpedition? expedition = Active;
        return expedition is null
            ? "No active Valewake mine expedition."
            : $"{expedition.ExpeditionId}: {expedition.NpcName} {expedition.Action} [{expedition.State}] " +
              $"mined={expedition.CompletedTargets}/{expedition.MaxTargets}, defeated={expedition.MonstersDefeated}";
    }

    private void FollowPlayer(MineExpedition expedition, NPC npc)
    {
        expedition.State = MineExpeditionStates.Following;
        if (TileDistance(npc.Tile, Game1.player.Tile) <= config.ExpeditionFollowDistanceTiles)
        {
            npc.controller = null;
            npc.Halt();
            ResetFollowRuntime(expedition, npc.Tile);
            return;
        }

        ActionTile npcTile = ToActionTile(npc.Tile);
        if (expedition.RuntimeLastNpcTile is null ||
            expedition.RuntimeLastNpcTile.X != npcTile.X ||
            expedition.RuntimeLastNpcTile.Y != npcTile.Y)
        {
            expedition.RuntimeLastNpcTile = npcTile;
            expedition.RuntimeLastProgressTick = Game1.ticks;
        }

        bool stalled = Game1.ticks - expedition.RuntimeLastProgressTick >= config.ExpeditionStallRecoveryTicks;
        bool targetMoved = expedition.RuntimeFollowTarget is null ||
            TileDistance(
                new Vector2(expedition.RuntimeFollowTarget.X, expedition.RuntimeFollowTarget.Y),
                Game1.player.Tile
            ) > config.ExpeditionFollowDistanceTiles + 1;
        bool repathDue = Game1.ticks - expedition.RuntimeFollowPathStartedTick >= config.ExpeditionRepathIntervalTicks;
        if (!stalled && !targetMoved && npc.controller is not null)
            return;
        if (!stalled && !repathDue)
            return;

        if (stalled)
        {
            expedition.RuntimeFollowCandidateIndex++;
            npc.controller = null;
            npc.Halt();
            if (Game1.ticks - expedition.RuntimeLastProgressTick >= config.ExpeditionOffscreenCatchUpTicks &&
                !Utility.isOnScreen(npc.Position, 96))
            {
                WarpNearPlayer(npc, expedition);
                ResetFollowRuntime(expedition, npc.Tile);
                Trace(expedition, "follow_stall_offscreen_recovered");
                return;
            }
            Trace(expedition, "follow_path_stalled");
            // Give the new candidate a full recovery window before declaring another stall.
            expedition.RuntimeLastProgressTick = Game1.ticks;
        }

        List<Vector2> candidates = FindOpenCandidatesNear(Game1.currentLocation, Game1.player.Tile, npc.Tile);
        if (candidates.Count == 0)
            return;
        Vector2 target = candidates[expedition.RuntimeFollowCandidateIndex % candidates.Count];
        expedition.RuntimeFollowTarget = ToActionTile(target);
        expedition.RuntimeFollowPathStartedTick = Game1.ticks;
        StartPath(npc, target);
    }

    private void UpdateCombat(MineExpedition expedition, NPC npc, Monster monster)
    {
        expedition.State = MineExpeditionStates.Defending;
        Vector2 monsterTile = monster.Tile;
        expedition.LastThreatType = monster.GetType().Name;
        expedition.LastThreatDistance = TileDistance(monsterTile, Game1.player.Tile);
        string navigationSubject = $"monster:{monster.GetHashCode()}:{(int)monsterTile.X}:{(int)monsterTile.Y}";
        if (!NavigateAdjacent(expedition, npc, monsterTile, navigationSubject))
            return;
        ResetTaskNavigation(expedition, clearController: false);
        npc.controller = null;
        npc.faceGeneralDirection(monster.Position, 0, opposite: false, useTileCalculations: false);
        NpcToolAnimation.PlayBodySwing(npc);
        int healthBefore = monster.Health;
        Game1.currentLocation.damageMonster(
            monster.GetBoundingBox(),
            config.ExpeditionAttackDamage,
            config.ExpeditionAttackDamage,
            isBomb: false,
            knockBackModifier: 1f,
            addedPrecision: 0,
            critChance: 0f,
            critMultiplier: 1f,
            triggerMonsterInvincibleTimer: true,
            who: Game1.player
        );
        Game1.playSound("swordswipe");
        expedition.RuntimeNextDecisionTick = Game1.ticks + 24;
        if (healthBefore > 0 && monster.Health <= 0)
        {
            expedition.MonstersDefeated++;
            Trace(expedition, "monster_defeated");
        }
    }

    private bool UpdateMining(MineExpedition expedition, NPC npc)
    {
        if (expedition.CompletedTargets >= expedition.MaxTargets)
        {
            if (expedition.EndAfterMining)
                BeginReturn(expedition, "The requested mining work is complete.");
            return false;
        }
        GameLocation location = Game1.currentLocation;
        Vector2? target = ResolveMiningTarget(expedition, location);
        if (target is null)
        {
            if (expedition.EndAfterMining)
                BeginReturn(expedition, "No eligible mine targets remain nearby.");
            return false;
        }
        expedition.State = MineExpeditionStates.Mining;
        expedition.RuntimeMiningTile = ToActionTile(target.Value);
        string navigationSubject = $"mine:{(int)target.Value.X}:{(int)target.Value.Y}";
        if (!NavigateAdjacent(expedition, npc, target.Value, navigationSubject))
        {
            if (expedition.RuntimeNavigationFailed)
            {
                expedition.RuntimeSkippedMineTiles.Add(MineTileKey(target.Value));
                expedition.RuntimeMiningTile = null;
                expedition.RuntimePriorityTile = null;
                Trace(expedition, "mine_target_unreachable");
                ResetTaskNavigation(expedition);
            }
            return true;
        }
        if (!location.Objects.TryGetValue(target.Value, out StardewValley.Object? node) || !IsAllowedMineNode(node))
        {
            expedition.RuntimeMiningTile = null;
            ResetTaskNavigation(expedition);
            return true;
        }
        expedition.LastTargetQualifiedId = node.QualifiedItemId ?? "";
        expedition.LastTargetName = node.Name ?? "";
        expedition.LastTargetAllowed = IsAllowedMineNode(node);
        npc.controller = null;
        npc.faceGeneralDirection(target.Value * 64f, 0, opposite: false, useTileCalculations: false);
        NpcToolAnimation.Play(npc, NpcToolKind.Pickaxe);
        Pickaxe pickaxe = new() { UpgradeLevel = Math.Clamp(Game1.player.MiningLevel / 2, 0, 4) };
        pickaxe.lastUser = Game1.player;
        bool destroyed = node.performToolAction(pickaxe);
        expedition.RuntimeMiningStrikes++;
        Game1.playSound("hammer");
        if (destroyed)
        {
            // Match Pickaxe.DoFunction so mine loot and ladder checks run through vanilla logic.
            location.OnStoneDestroyed(node.ItemId, (int)target.Value.X, (int)target.Value.Y, Game1.player);
            node.performRemoveAction();
            location.Objects.Remove(target.Value);
            expedition.CompletedTargets++;
            expedition.RuntimeMiningTile = null;
            expedition.RuntimePriorityTile = null;
            expedition.RuntimeMiningStrikes = 0;
            ResetTaskNavigation(expedition);
            Save();
            Trace(expedition, "node_mined");
        }
        else if (expedition.RuntimeMiningStrikes >= 12)
        {
            expedition.RuntimeMiningTile = null;
            expedition.RuntimePriorityTile = null;
            expedition.RuntimeMiningStrikes = 0;
            ResetTaskNavigation(expedition);
            Trace(expedition, "node_skipped_strike_limit");
        }
        expedition.RuntimeNextDecisionTick = Game1.ticks + 30;
        return true;
    }

    private Vector2? ResolveMiningTarget(MineExpedition expedition, GameLocation location)
    {
        if (expedition.RuntimeMiningTile is not null)
        {
            Vector2 current = new(expedition.RuntimeMiningTile.X, expedition.RuntimeMiningTile.Y);
            if (location.Objects.TryGetValue(current, out StardewValley.Object? currentNode) && IsAllowedMineNode(currentNode))
                return current;
            expedition.RuntimeMiningTile = null;
            expedition.RuntimeMiningStrikes = 0;
        }
        if (expedition.RuntimePriorityTile is not null)
        {
            Vector2 priority = new(expedition.RuntimePriorityTile.X, expedition.RuntimePriorityTile.Y);
            if (location.Objects.TryGetValue(priority, out StardewValley.Object? priorityNode) && IsAllowedMineNode(priorityNode))
                return priority;
            expedition.RuntimePriorityTile = null;
        }
        if (expedition.MiningMode == "target")
            return null;
        return location.Objects.Pairs
            .Where(pair => IsAllowedMineNode(pair.Value))
            .Where(pair => !expedition.RuntimeSkippedMineTiles.Contains(MineTileKey(pair.Key)))
            .Where(pair => TileDistance(pair.Key, Game1.player.Tile) <= config.ExpeditionMiningRadiusTiles)
            .OrderBy(pair => PriorityRank(pair.Value, expedition.ResourcePriority))
            .ThenBy(pair => TileDistance(pair.Key, Game1.player.Tile))
            .Select(pair => (Vector2?)pair.Key)
            .FirstOrDefault();
    }

    private static Monster? FindThreat(GameLocation location, Vector2 playerTile, int radius) =>
        location.characters
            .OfType<Monster>()
            .Where(monster => monster.Health > 0 && TileDistance(monster.Tile, playerTile) <= radius)
            .OrderBy(monster => TileDistance(monster.Tile, playerTile))
            .FirstOrDefault();

    private void WarpNearPlayer(NPC npc, MineExpedition expedition)
    {
        Vector2 tile = FindOpenNear(Game1.currentLocation, Game1.player.Tile, Game1.player.Tile);
        Game1.warpCharacter(npc, Game1.currentLocation, tile);
        ReserveNpc(npc, expedition);
    }

    private void WarpAtArrival(NPC npc, string previousLocation)
    {
        MineExpedition? expedition = Active;
        if (expedition is null)
            return;
        Vector2 tile = FindArrivalTile(Game1.currentLocation, previousLocation, Game1.player.Tile);
        Game1.warpCharacter(npc, Game1.currentLocation, tile);
        ReserveNpc(npc, expedition);
        ResetFollowRuntime(expedition, npc.Tile);
    }

    private void BeginReturn(MineExpedition expedition, string reason)
    {
        expedition.State = MineExpeditionStates.Returning;
        expedition.LastMessage = reason;
        Save();
        Trace(expedition, "returning");
        Game1.addHUDMessage(new HUDMessage($"{expedition.NpcName}: {reason}"));
    }

    private void UpdateReturning(MineExpedition expedition, NPC npc)
    {
        // In a mine level, keep the NPC visible until the player reaches a normal map.
        if (npc.currentLocation == Game1.currentLocation && Game1.currentLocation is MineShaft)
        {
            if (TileDistance(npc.Tile, Game1.player.Tile) > config.ExpeditionFollowDistanceTiles)
                StartPath(npc, FindOpenNear(Game1.currentLocation, Game1.player.Tile, npc.Tile));
            return;
        }
        if (npc.currentLocation == Game1.currentLocation)
        {
            Vector2 destination;
            bool sameAsOrigin = string.Equals(
                expedition.ReturnContext.LocationName,
                Game1.currentLocation.NameOrUniqueName,
                StringComparison.Ordinal
            );
            if (sameAsOrigin)
            {
                destination = new Vector2(expedition.ReturnContext.TileX, expedition.ReturnContext.TileY);
            }
            else
            {
                destination = FindVisibleExit(Game1.currentLocation, npc.Tile);
            }
            expedition.RuntimeReturnTile ??= ToActionTile(destination);
            destination = new Vector2(expedition.RuntimeReturnTile.X, expedition.RuntimeReturnTile.Y);
            if (TileDistance(npc.Tile, destination) > 1)
            {
                if (npc.controller is null || Game1.ticks - expedition.RuntimeReturnPathStartedTick > 90)
                {
                    expedition.RuntimeReturnPathStartedTick = Game1.ticks;
                    StartPath(npc, destination);
                }
                return;
            }
        }
        RestoreNpc(expedition);
        expedition.State = MineExpeditionStates.Completed;
        Save();
        Trace(expedition, "completed");
        Game1.addHUDMessage(new HUDMessage(
            $"{expedition.NpcName} left the expedition: {expedition.CompletedTargets} mined, {expedition.MonstersDefeated} monsters defeated."
        ));
    }

    private void Fail(MineExpedition expedition, string reason)
    {
        expedition.State = MineExpeditionStates.Failed;
        expedition.LastMessage = reason;
        RestoreNpc(expedition);
        Save();
        Trace(expedition, "failed");
        monitor.Log($"Mine expedition {expedition.ExpeditionId} failed: {reason}", LogLevel.Warn);
    }

    private void RestoreNpc(MineExpedition expedition)
    {
        NPC? npc = Game1.getCharacterFromName(expedition.NpcName);
        if (npc is null)
            return;
        npc.controller = null;
        npc.Halt();
        GameLocation? location = Game1.getLocationFromName(expedition.ReturnContext.LocationName);
        if (location is not null)
            Game1.warpCharacter(npc, location, new Vector2(expedition.ReturnContext.TileX, expedition.ReturnContext.TileY));
        npc.FacingDirection = expedition.ReturnContext.FacingDirection;
        npc.addedSpeed = expedition.OriginalAddedSpeed;
        npc.Speed = Math.Max(1, expedition.OriginalSpeed);
        npc.followSchedule = expedition.ReturnContext.FollowSchedule;
        if (npc.followSchedule)
            npc.checkSchedule(Game1.timeOfDay);
    }

    private void ReserveNpc(NPC npc, MineExpedition expedition)
    {
        npc.controller = null;
        npc.Halt();
        npc.followSchedule = false;
        MaintainReservation(npc, expedition);
    }

    private void MaintainReservation(NPC npc, MineExpedition expedition)
    {
        npc.followSchedule = false;
        npc.addedSpeed = expedition.OriginalAddedSpeed;
        npc.Speed = Math.Max(1, expedition.OriginalSpeed + Math.Max(0, config.ExpeditionFollowSpeedBoost));
    }

    private bool NavigateAdjacent(
        MineExpedition expedition,
        NPC npc,
        Vector2 subjectTile,
        string subjectKey)
    {
        if (TileDistance(npc.Tile, subjectTile) <= 1)
        {
            npc.controller = null;
            npc.Halt();
            return true;
        }

        ActionTile npcTile = ToActionTile(npc.Tile);
        bool subjectChanged = !string.Equals(
            expedition.RuntimeNavigationSubject,
            subjectKey,
            StringComparison.Ordinal
        );
        if (subjectChanged)
        {
            expedition.RuntimeNavigationSubject = subjectKey;
            expedition.RuntimeApproachTile = null;
            expedition.RuntimeNavigationCandidateIndex = 0;
            expedition.RuntimeNavigationLastNpcTile = npcTile;
            expedition.RuntimeNavigationLastProgressTick = Game1.ticks;
            expedition.RuntimeNavigationPathStartedTick = 0;
            expedition.RuntimeNavigationFailed = false;
            npc.controller = null;
            npc.Halt();
        }
        else if (expedition.RuntimeNavigationLastNpcTile is null ||
                 expedition.RuntimeNavigationLastNpcTile.X != npcTile.X ||
                 expedition.RuntimeNavigationLastNpcTile.Y != npcTile.Y)
        {
            expedition.RuntimeNavigationLastNpcTile = npcTile;
            expedition.RuntimeNavigationLastProgressTick = Game1.ticks;
        }

        bool stalled = Game1.ticks - expedition.RuntimeNavigationLastProgressTick >=
                       config.ExpeditionStallRecoveryTicks;
        bool retryDue = npc.controller is null &&
                        Game1.ticks - expedition.RuntimeNavigationPathStartedTick >=
                        config.ExpeditionRepathIntervalTicks;
        if (!subjectChanged && !stalled && !retryDue)
            return false;

        List<Vector2> candidates = FindAdjacentCandidates(Game1.currentLocation, subjectTile, npc.Tile);
        if (candidates.Count == 0)
        {
            if (subjectChanged || stalled)
                Trace(expedition, "navigation_no_adjacent_tile");
            expedition.RuntimeNavigationLastProgressTick = Game1.ticks;
            expedition.RuntimeNavigationFailed = true;
            return false;
        }

        if (stalled || (!subjectChanged && npc.controller is null))
            expedition.RuntimeNavigationCandidateIndex++;
        if (stalled && expedition.RuntimeNavigationCandidateIndex >= candidates.Count)
        {
            expedition.RuntimeNavigationFailed = true;
            Trace(expedition, "navigation_candidates_exhausted");
            return false;
        }
        Vector2 destination = candidates[
            expedition.RuntimeNavigationCandidateIndex % candidates.Count
        ];
        expedition.RuntimeApproachTile = ToActionTile(destination);
        expedition.RuntimeNavigationPathStartedTick = Game1.ticks;
        expedition.RuntimeNavigationLastProgressTick = Game1.ticks;
        npc.controller = null;
        npc.Halt();
        StartPath(npc, destination);
        if (stalled)
            Trace(expedition, "navigation_stall_recovered");
        return false;
    }

    private static List<Vector2> FindAdjacentCandidates(
        GameLocation location,
        Vector2 subjectTile,
        Vector2 npcTile)
    {
        Vector2[] candidates =
        {
            subjectTile + new Vector2(0, -1),
            subjectTile + new Vector2(1, 0),
            subjectTile + new Vector2(0, 1),
            subjectTile + new Vector2(-1, 0)
        };
        return candidates
            .Where(tile => tile == npcTile || IsWalkable(location, tile))
            .OrderBy(tile => TileDistance(tile, npcTile))
            .ToList();
    }

    private static void ResetTaskNavigation(MineExpedition expedition, bool clearController = true)
    {
        bool hadTaskNavigation = !string.IsNullOrWhiteSpace(expedition.RuntimeNavigationSubject);
        expedition.RuntimeNavigationSubject = "";
        expedition.RuntimeApproachTile = null;
        expedition.RuntimeNavigationLastNpcTile = null;
        expedition.RuntimeNavigationPathStartedTick = 0;
        expedition.RuntimeNavigationLastProgressTick = Game1.ticks;
        expedition.RuntimeNavigationCandidateIndex = 0;
        expedition.RuntimeNavigationFailed = false;
        if (clearController && hadTaskNavigation)
        {
            NPC? npc = Game1.getCharacterFromName(expedition.NpcName);
            if (npc is not null)
            {
                npc.controller = null;
                npc.Halt();
            }
        }
    }

    private static void StartPath(NPC npc, Vector2 target)
    {
        if (npc.controller is not null && TileDistance(npc.controller.endPoint.ToVector2(), target) <= 1)
            return;
        npc.controller = new PathFindController(npc, npc.currentLocation, target.ToPoint(), 2);
    }

    private static Vector2 FindOpenNear(GameLocation location, Vector2 center, Vector2 from)
    {
        return FindOpenCandidatesNear(location, center, from).FirstOrDefault(center);
    }

    private static List<Vector2> FindOpenCandidatesNear(GameLocation location, Vector2 center, Vector2 from)
    {
        List<Vector2> candidates = new();
        for (int radius = 1; radius <= 3; radius++)
        {
            for (int y = (int)center.Y - radius; y <= center.Y + radius; y++)
            for (int x = (int)center.X - radius; x <= center.X + radius; x++)
            {
                Vector2 tile = new(x, y);
                if (Math.Abs(x - center.X) + Math.Abs(y - center.Y) == radius &&
                    IsWalkable(location, tile))
                    candidates.Add(tile);
            }
        }
        return candidates
            .Distinct()
            .OrderBy(tile => TileDistance(tile, from))
            .ToList();
    }

    private static KeyValuePair<Vector2, StardewValley.Object>? FindMineNodeNearCursor(
        GameLocation location,
        Vector2 cursorTile,
        int snapRadius)
    {
        if (location.Objects.TryGetValue(cursorTile, out StardewValley.Object? exact) && IsAllowedMineNode(exact))
            return new KeyValuePair<Vector2, StardewValley.Object>(cursorTile, exact);

        int radius = Math.Max(0, snapRadius);
        return location.Objects.Pairs
            .Where(pair => IsAllowedMineNode(pair.Value))
            .Where(pair => Math.Max(
                Math.Abs((int)pair.Key.X - (int)cursorTile.X),
                Math.Abs((int)pair.Key.Y - (int)cursorTile.Y)
            ) <= radius)
            .OrderBy(pair => Vector2.DistanceSquared(pair.Key, cursorTile))
            .Cast<KeyValuePair<Vector2, StardewValley.Object>?>()
            .FirstOrDefault();
    }

    private static Vector2 FindArrivalTile(GameLocation location, string previousLocation, Vector2 playerTile)
    {
        Warp? reverseWarp = location.warps.FirstOrDefault(warp =>
            warp.TargetName.Equals(previousLocation, StringComparison.OrdinalIgnoreCase) ||
            previousLocation.StartsWith(warp.TargetName, StringComparison.OrdinalIgnoreCase)
        );
        if (reverseWarp is not null)
        {
            Vector2 warpTile = new(reverseWarp.X, reverseWarp.Y);
            List<Vector2> candidates = FindOpenCandidatesNear(location, warpTile, playerTile);
            if (candidates.Count > 0)
                return candidates.OrderBy(tile => TileDistance(tile, warpTile)).First();
        }
        return FindOpenNear(location, playerTile, playerTile);
    }

    private static Vector2 FindVisibleExit(GameLocation location, Vector2 from)
    {
        Vector2? warp = location.warps
            .Select(item => new Vector2(item.X, item.Y))
            .Where(tile => IsWalkable(location, tile))
            .OrderBy(tile => TileDistance(tile, from))
            .Cast<Vector2?>()
            .FirstOrDefault();
        if (warp is not null)
            return warp.Value;

        List<Vector2> edgeTiles = new();
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        for (int x = 0; x < width; x++)
        {
            edgeTiles.Add(new Vector2(x, 0));
            edgeTiles.Add(new Vector2(x, height - 1));
        }
        for (int y = 1; y < height - 1; y++)
        {
            edgeTiles.Add(new Vector2(0, y));
            edgeTiles.Add(new Vector2(width - 1, y));
        }
        return edgeTiles.Where(tile => IsWalkable(location, tile))
            .OrderBy(tile => TileDistance(tile, from))
            .FirstOrDefault(from);
    }

    private static bool IsWalkable(GameLocation location, Vector2 tile)
    {
        int x = (int)tile.X;
        int y = (int)tile.Y;
        if (x < 0 || y < 0 || x >= location.Map.Layers[0].LayerWidth || y >= location.Map.Layers[0].LayerHeight)
            return false;
        if (!location.isTilePassable(new Location(x, y), Game1.viewport))
            return false;
        if (location.Objects.TryGetValue(tile, out StardewValley.Object? obj) && !obj.isPassable())
            return false;
        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature) && !feature.isPassable())
            return false;
        return location.characters.All(character => character.Tile != tile);
    }

    private static bool IsAllowedMineNode(StardewValley.Object obj) =>
        !obj.bigCraftable.Value && obj.IsBreakableStone();

    private static int PriorityRank(StardewValley.Object obj, string priority)
    {
        if (priority == "any")
            return 0;
        string id = obj.QualifiedItemId;
        string name = obj.Name;
        bool match = priority switch
        {
            "copper" => id == "(O)751" || name.Contains("Copper", StringComparison.OrdinalIgnoreCase),
            "iron" => id == "(O)290" || name.Contains("Iron", StringComparison.OrdinalIgnoreCase),
            "gold" => id == "(O)764" || name.Contains("Gold", StringComparison.OrdinalIgnoreCase),
            "iridium" => id == "(O)765" || name.Contains("Iridium", StringComparison.OrdinalIgnoreCase),
            "stone" => name.Equals("Stone", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return match ? 0 : 1;
    }

    private static bool CanDefend(MineExpedition expedition) => expedition.DefenseEnabled;

    private static bool CanMine(MineExpedition expedition) => expedition.MiningEnabled;

    private bool AddsCapability(MineExpedition expedition, string action) => action switch
    {
        ActionIds.JoinMineExpedition => false,
        ActionIds.DefendPlayer => !expedition.DefenseEnabled,
        ActionIds.MineTarget => expedition.CompletedTargets < config.ExpeditionMaxMineTargets,
        ActionIds.MineNearby => !expedition.MiningEnabled || expedition.MiningMode != "nearby",
        ActionIds.MineExpedition => !expedition.DefenseEnabled || !expedition.MiningEnabled ||
                                    expedition.MiningMode != "nearby",
        _ => false
    };

    private static string CanonicalAction(MineExpedition expedition, string requestedAction)
    {
        if (expedition.DefenseEnabled && expedition.MiningEnabled)
            return ActionIds.MineExpedition;
        if (expedition.DefenseEnabled)
            return ActionIds.DefendPlayer;
        if (expedition.MiningEnabled)
            return expedition.MiningMode == "target" ? ActionIds.MineTarget : ActionIds.MineNearby;
        return requestedAction == ActionIds.JoinMineExpedition
            ? ActionIds.JoinMineExpedition
            : expedition.Action;
    }

    private void TryAssignCursorTarget(MineExpedition expedition)
    {
        if (Game1.currentLocation is not MineShaft)
            return;
        Vector2 tile = helper.Input.GetCursorPosition().GrabTile;
        KeyValuePair<Vector2, StardewValley.Object>? selected = FindMineNodeNearCursor(
            Game1.currentLocation,
            tile,
            config.ExpeditionTargetSnapRadiusTiles
        );
        if (selected is not null)
            expedition.RuntimePriorityTile = ToActionTile(selected.Value.Key);
    }

    private void TraceTargetSelection(
        MineExpedition expedition,
        Vector2 cursorTile,
        Vector2? selectedTile,
        string result)
    {
        object[] nearbyObjects = Game1.currentLocation.Objects.Pairs
            .Where(pair => Math.Max(
                Math.Abs((int)pair.Key.X - (int)cursorTile.X),
                Math.Abs((int)pair.Key.Y - (int)cursorTile.Y)
            ) <= Math.Max(2, config.ExpeditionTargetSnapRadiusTiles))
            .OrderBy(pair => Vector2.DistanceSquared(pair.Key, cursorTile))
            .Take(8)
            .Select(pair => (object)new
            {
                x = (int)pair.Key.X,
                y = (int)pair.Key.Y,
                qualified_id = pair.Value.QualifiedItemId,
                name = pair.Value.Name,
                breakable_stone = pair.Value.IsBreakableStone(),
                big_craftable = pair.Value.bigCraftable.Value
            })
            .ToArray();
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = "manual_target_selection",
            expedition_id = expedition.ExpeditionId,
            save_id = expedition.SaveId,
            npc_name = expedition.NpcName,
            result,
            cursor_tile = new { x = (int)cursorTile.X, y = (int)cursorTile.Y },
            selected_tile = selectedTile is null
                ? null
                : new { x = (int)selectedTile.Value.X, y = (int)selectedTile.Value.Y },
            snap_radius = config.ExpeditionTargetSnapRadiusTiles,
            nearby_objects = nearbyObjects
        });
    }

    private static void ResetFollowRuntime(MineExpedition expedition, Vector2 npcTile)
    {
        expedition.RuntimeFollowTarget = null;
        expedition.RuntimeLastNpcTile = ToActionTile(npcTile);
        expedition.RuntimeFollowPathStartedTick = 0;
        expedition.RuntimeLastProgressTick = Game1.ticks;
        expedition.RuntimeFollowCandidateIndex = 0;
    }

    private MineExpedition? Active => data.Expeditions.LastOrDefault(item => !MineExpeditionStates.IsTerminal(item.State));
    private void Save() => helper.Data.WriteSaveData(SaveDataKey, data);

    private void Trace(MineExpedition expedition, string eventName)
    {
        NPC? npc = Game1.getCharacterFromName(expedition.NpcName);
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = eventName,
            turn_id = string.IsNullOrWhiteSpace(expedition.LastCommandTurnId)
                ? expedition.SourceTurnId
                : expedition.LastCommandTurnId,
            proposal_id = string.IsNullOrWhiteSpace(expedition.LastCommandProposalId)
                ? expedition.ProposalId
                : expedition.LastCommandProposalId,
            root_turn_id = expedition.SourceTurnId,
            root_proposal_id = expedition.ProposalId,
            expedition_id = expedition.ExpeditionId,
            save_id = expedition.SaveId,
            npc_name = expedition.NpcName,
            action = expedition.Action,
            defense_enabled = expedition.DefenseEnabled,
            mining_enabled = expedition.MiningEnabled,
            mining_mode = expedition.MiningMode,
            state = expedition.State,
            resource_priority = expedition.ResourcePriority,
            completed_targets = expedition.CompletedTargets,
            monsters_defeated = expedition.MonstersDefeated,
            target_qualified_id = expedition.LastTargetQualifiedId,
            target_name = expedition.LastTargetName,
            target_allowed = expedition.LastTargetAllowed,
            threat_type = expedition.LastThreatType,
            threat_distance = expedition.LastThreatDistance,
            npc_location = npc?.currentLocation?.NameOrUniqueName,
            return_location = expedition.ReturnContext.LocationName,
            location_restored = npc is not null && string.Equals(
                npc.currentLocation?.NameOrUniqueName,
                expedition.ReturnContext.LocationName,
                StringComparison.Ordinal
            ),
            schedule_restored = npc is not null && npc.followSchedule == expedition.ReturnContext.FollowSchedule,
            player_location = Context.IsWorldReady ? Game1.currentLocation?.NameOrUniqueName : null,
            message = expedition.LastMessage
        });
    }

    private void AppendTrace(object record)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
            File.AppendAllText(tracePath, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not append Expedition Trace: {ex.Message}", LogLevel.Warn);
        }
    }

    private static int GetRequestedMaximum(AgentActionProposal proposal) =>
        proposal.Parameters.TryGetValue("max_targets", out JsonElement value) && value.TryGetInt32(out int maximum)
            ? Math.Clamp(maximum, 1, 10)
            : 10;

    private static string GetStringParameter(AgentActionProposal proposal, string key, string fallback) =>
        proposal.Parameters.TryGetValue(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static ActionTile ToActionTile(Vector2 tile) => new((int)tile.X, (int)tile.Y);
    private static string MineTileKey(Vector2 tile) => $"{(int)tile.X}:{(int)tile.Y}";
    private static int TileDistance(Vector2 left, Vector2 right) =>
        Math.Abs((int)left.X - (int)right.X) + Math.Abs((int)left.Y - (int)right.Y);
}
