using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using xTile.Dimensions;

namespace Valewake;

public sealed class ActionJobManager
{
    private const string SaveDataKey = "valewake-action-jobs-v1";
    private static readonly HashSet<string> WeedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "(O)0", "(O)2", "(O)4", "(O)6", "(O)8", "(O)10", "(O)12", "(O)14", "(O)16",
        "(O)313", "(O)314", "(O)315", "(O)316", "(O)317", "(O)318", "(O)319"
    };

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly string tracePath;
    private ActionJobSaveData data = new();

    public ActionJobManager(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        tracePath = Path.Combine(helper.DirectoryPath, "data", "traces", "action_trace.jsonl");
    }

    public void Load()
    {
        data = helper.Data.ReadSaveData<ActionJobSaveData>(SaveDataKey) ?? new ActionJobSaveData();
        foreach (ActionJob job in data.Jobs.Where(job => !ActionJobStates.IsTerminal(job.State)))
        {
            job.State = ActionJobStates.FailedRecoverable;
            job.LastMessage = "The game was reloaded before the job completed.";
            RestoreNpc(job);
            Trace(job, "recovered_after_reload");
        }
        Save();
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
            return ActionProposalDecision.Reject("Only the multiplayer host can authorize NPC jobs.");
        if (Game1.eventUp || Game1.currentLocation?.currentEvent is not null || Game1.isFestival())
            return ActionProposalDecision.Reject("NPC jobs cannot start during an event or festival.");
        if (npc.Age == 2)
            return ActionProposalDecision.Reject("Child NPCs cannot be assigned farm jobs.");
        if (!string.Equals(proposal.Disposition, "accept", StringComparison.OrdinalIgnoreCase))
            return ActionProposalDecision.Reject($"{npc.displayName} did not accept the job.");
        if (proposal.Confidence < config.MinimumActionConfidence)
            return ActionProposalDecision.Reject("The action proposal was not confident enough.");
        if (string.IsNullOrWhiteSpace(proposal.Evidence) ||
            !playerInput.Contains(proposal.Evidence, StringComparison.OrdinalIgnoreCase))
        {
            return ActionProposalDecision.Reject("The proposal was not grounded in the player's request.");
        }
        if (Game1.timeOfDay >= 2200)
            return ActionProposalDecision.Reject("It is too late to begin a farm job.");
        if (GetActiveJob(npc.Name) is not null)
            return ActionProposalDecision.Reject($"{npc.displayName} already has an active job.");

        int hearts = 0;
        if (Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship))
            hearts = friendship.Points / 250;
        if (hearts < config.MinimumActionHearts)
        {
            return ActionProposalDecision.Reject(
                $"This job requires at least {config.MinimumActionHearts} hearts."
            );
        }
        if (relationship.Trust < config.MinimumActionTrust)
        {
            return ActionProposalDecision.Reject(
                $"This job requires at least {config.MinimumActionTrust} Valewake trust."
            );
        }

        int requestedMaximum = GetRequestedMaximum(proposal);
        return proposal.Action switch
        {
            ActionIds.WaterCrops when config.EnableWaterCropsAction =>
                ActionProposalDecision.Allow(
                    $"Let {npc.displayName} water up to {Math.Min(requestedMaximum, config.MaxWaterTilesPerJob)} nearby crop tiles?",
                    Math.Min(requestedMaximum, config.MaxWaterTilesPerJob)
                ),
            ActionIds.ClearWeeds when config.EnableClearWeedsAction =>
                ActionProposalDecision.Allow(
                    $"Let {npc.displayName} clear up to {Math.Min(requestedMaximum, config.MaxWeedsPerJob)} nearby weeds?",
                    Math.Min(requestedMaximum, config.MaxWeedsPerJob)
                ),
            ActionIds.WaterCrops =>
                ActionProposalDecision.Reject("Watering jobs are disabled."),
            ActionIds.ClearWeeds =>
                ActionProposalDecision.Reject("Weeding jobs are disabled."),
            _ => ActionProposalDecision.Reject("That action is not on the local allowlist.")
        };
    }

    public ActionJob Accept(
        AgentActionProposal proposal,
        NPC npc,
        ActionProposalDecision decision)
    {
        bool playerIsOnFarm = Game1.player.currentLocation is Farm;
        ActionJob job = new()
        {
            JobId = $"job_{Guid.NewGuid():N}"[..16],
            ProposalId = proposal.ProposalId,
            SaveId = Constants.SaveFolderName ?? "",
            NpcName = npc.Name,
            Action = proposal.Action,
            State = ActionJobStates.Accepted,
            MaxTargets = decision.MaxTargets,
            CreatedDay = Game1.Date.TotalDays,
            CreatedTime = Game1.timeOfDay,
            AnchorX = playerIsOnFarm ? (int)Game1.player.Tile.X : -1,
            AnchorY = playerIsOnFarm ? (int)Game1.player.Tile.Y : -1,
            ReturnContext = new ActionReturnContext
            {
                OriginKind = npc.currentLocation switch
                {
                    Farm => ActionOriginKinds.Farm,
                    StardewValley.Locations.FarmHouse => ActionOriginKinds.FarmHouse,
                    _ => ActionOriginKinds.Boundary
                },
                LocationName = npc.currentLocation?.NameOrUniqueName ?? "",
                TileX = (int)npc.Tile.X,
                TileY = (int)npc.Tile.Y,
                FacingDirection = npc.FacingDirection,
                FollowSchedule = npc.followSchedule
            }
        };
        data.Jobs.Add(job);
        Save();
        Trace(job, "accepted");
        monitor.Log(
            $"Accepted action job {job.JobId}: {job.NpcName}/{job.Action}/{job.MaxTargets}.",
            LogLevel.Info
        );
        return job;
    }

    public void TraceProposalDecision(
        AgentActionProposal proposal,
        NPC npc,
        ActionProposalDecision decision)
    {
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = "proposal_validated",
            proposal_id = proposal.ProposalId,
            npc_name = npc.Name,
            action = proposal.Action,
            disposition = proposal.Disposition,
            confidence = proposal.Confidence,
            evidence = proposal.Evidence,
            allowed = decision.Allowed,
            message = decision.Message,
            max_targets = decision.MaxTargets
        });
    }

    public void TraceConfirmation(
        AgentActionProposal proposal,
        NPC npc,
        bool confirmed)
    {
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = confirmed ? "confirmation_accepted" : "confirmation_declined",
            proposal_id = proposal.ProposalId,
            npc_name = npc.Name,
            action = proposal.Action,
            confirmed
        });
    }

    public void Update()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        foreach (ActionJob job in data.Jobs.Where(job => !ActionJobStates.IsTerminal(job.State)).ToList())
            UpdateJob(job);
    }

    public string GetSummary()
    {
        List<ActionJob> active = data.Jobs
            .Where(job => !ActionJobStates.IsTerminal(job.State))
            .ToList();
        if (active.Count == 0)
            return "No active Valewake action jobs.";
        return string.Join(
            Environment.NewLine,
            active.Select(job =>
                $"{job.JobId}: {job.NpcName} {job.Action} [{job.State}] " +
                $"{job.CompletedTargets}/{job.Targets.Count}"
            )
        );
    }

    public void CancelAll(string reason)
    {
        foreach (ActionJob job in data.Jobs.Where(job => !ActionJobStates.IsTerminal(job.State)))
        {
            job.State = ActionJobStates.Cancelled;
            job.LastMessage = reason;
            RestoreNpc(job);
            Trace(job, "cancelled");
        }
        Save();
    }

    private void UpdateJob(ActionJob job)
    {
        if (Game1.eventUp || Game1.currentLocation?.currentEvent is not null || Game1.isFestival())
        {
            Fail(job, "An event or festival interrupted the job.", terminal: false);
            return;
        }
        if (Game1.Date.TotalDays != job.CreatedDay || Game1.timeOfDay >= 2200)
        {
            Fail(job, "The work window ended before the job completed.", terminal: false);
            return;
        }

        NPC? npc = Game1.getCharacterFromName(job.NpcName);
        if (npc is null)
        {
            Fail(job, "NPC could not be found.", terminal: true);
            return;
        }

        Farm farm = Game1.getFarm();
        switch (job.State)
        {
            case ActionJobStates.Accepted:
                ReserveNpc(npc);
                if (npc.currentLocation is Farm)
                {
                    Transition(job, ActionJobStates.Preparing, "NPC is already on the farm.");
                }
                else if (!config.EnableCrossMapDispatch)
                {
                    Fail(job, "Cross-map dispatch is disabled.", terminal: false);
                }
                else
                {
                    job.RuntimeNextTick = Game1.ticks + 60;
                    job.RuntimeDispatchDeadlineTick =
                        Game1.ticks + Math.Max(10, config.CrossMapDispatchWaitSeconds) * 60L;
                    Transition(job, ActionJobStates.Dispatching, "Waiting for the player to enter the farm.");
                }
                break;

            case ActionJobStates.Dispatching:
                if (Game1.ticks < job.RuntimeNextTick)
                    return;
                if (Game1.player.currentLocation is not Farm)
                {
                    if (Game1.ticks >= job.RuntimeDispatchDeadlineTick)
                        Fail(job, "The player did not reach the farm before dispatch timed out.", terminal: false);
                    return;
                }
                if (job.RuntimeFarmArrivalTick == 0)
                {
                    job.RuntimeFarmArrivalTick = Game1.ticks;
                    Game1.addHUDMessage(new HUDMessage(
                        $"Head toward the work area; {npc.displayName} will join you shortly."
                    ));
                    return;
                }
                if (Game1.ticks - job.RuntimeFarmArrivalTick <
                    Math.Max(0, config.CrossMapFarmLeadSeconds) * 60L)
                {
                    return;
                }
                ReserveNpc(npc);
                job.AnchorX = (int)Game1.player.Tile.X;
                job.AnchorY = (int)Game1.player.Tile.Y;
                Vector2 entry = job.ReturnContext.OriginKind == ActionOriginKinds.FarmHouse
                    ? FindFarmHouseExteriorEntry(farm)
                    : FindFarmBoundaryEntry(farm);
                job.RuntimeDispatchTile = new ActionTile((int)entry.X, (int)entry.Y);
                Game1.warpCharacter(npc, farm, entry);
                Game1.addHUDMessage(new HUDMessage(
                    $"{npc.displayName} arrived at the farm and is walking to the work area."
                ));
                Transition(
                    job,
                    ActionJobStates.Preparing,
                    job.ReturnContext.OriginKind == ActionOriginKinds.FarmHouse
                        ? "NPC came out through the farmhouse door."
                        : "NPC entered through the farm boundary."
                );
                break;

            case ActionJobStates.Preparing:
                if (npc.currentLocation is not Farm)
                {
                    Fail(job, "NPC left the farm before work began.", terminal: false);
                    return;
                }
                if (job.Targets.Count == 0)
                    job.Targets = FindTargets(job, farm);
                if (job.Targets.Count == 0)
                {
                    BeginReturn(job, npc, farm, "No eligible targets were found.");
                    return;
                }
                StartNextTarget(job, npc, farm);
                break;

            case ActionJobStates.Navigating:
                UpdateNavigation(job, npc, farm);
                break;

            case ActionJobStates.Acting:
                if (Game1.ticks < job.RuntimeNextTick)
                    return;
                ExecuteCurrentTarget(job, npc, farm);
                break;

            case ActionJobStates.Returning:
                UpdateReturn(job, npc, farm);
                break;
        }
    }

    private void StartNextTarget(ActionJob job, NPC npc, Farm farm)
    {
        while (job.CurrentTargetIndex < job.Targets.Count)
        {
            ActionTile target = job.Targets[job.CurrentTargetIndex];
            job.RuntimeStandCandidates = FindStandTiles(farm, target, npc.Tile);
            job.RuntimeStandCandidateIndex = 0;
            if (job.RuntimeStandCandidates.Count == 0)
            {
                job.FailedTargets++;
                job.CurrentTargetIndex++;
                continue;
            }

            StartPathToCurrentStand(job, npc, farm);
            Transition(
                job,
                ActionJobStates.Navigating,
                $"Moving to target {job.CurrentTargetIndex + 1}/{job.Targets.Count}."
            );
            return;
        }

        BeginReturn(
            job,
            npc,
            farm,
            $"Completed {job.CompletedTargets} targets; skipped {job.FailedTargets}."
        );
    }

    private void UpdateNavigation(ActionJob job, NPC npc, Farm farm)
    {
        if (npc.currentLocation is not Farm || job.RuntimeStandTile is null)
        {
            Fail(job, "NPC or target left the farm during navigation.", terminal: false);
            return;
        }

        ActionTile stand = job.RuntimeStandTile;
        if ((int)npc.Tile.X == stand.X && (int)npc.Tile.Y == stand.Y)
        {
            npc.controller = null;
            npc.Halt();
            ActionTile target = job.Targets[job.CurrentTargetIndex];
            npc.FacingDirection = FacingToward(stand, target);
            StartWorkAnimation(npc);
            Game1.playSound(job.Action == ActionIds.WaterCrops ? "wateringCan" : "cut");
            job.RuntimeActionStartedTick = Game1.ticks;
            job.RuntimeNextTick = Game1.ticks + 54;
            Transition(job, ActionJobStates.Acting, "Playing the work animation.");
            return;
        }

        if (Game1.ticks - job.RuntimePathStartedTick > config.ActionPathTimeoutTicks ||
            npc.controller is null)
        {
            npc.controller = null;
            npc.Halt();
            job.RuntimeStandCandidateIndex++;
            if (job.RuntimeStandCandidateIndex < job.RuntimeStandCandidates.Count)
            {
                StartPathToCurrentStand(job, npc, farm);
                job.LastMessage = "Path failed; trying another adjacent work tile.";
                Save();
                Trace(job, "target_path_retry");
                return;
            }
            job.FailedTargets++;
            if (job.CompletedTargets == 0 &&
                job.FailedTargets == 1 &&
                job.RuntimeDispatchTile is not null &&
                (int)npc.Tile.X == job.RuntimeDispatchTile.X &&
                (int)npc.Tile.Y == job.RuntimeDispatchTile.Y)
            {
                Fail(
                    job,
                    $"The farm entrance at ({job.RuntimeDispatchTile.X}, {job.RuntimeDispatchTile.Y}) " +
                    "was not connected to the selected work area.",
                    terminal: false
                );
                return;
            }
            job.CurrentTargetIndex++;
            job.State = ActionJobStates.Preparing;
            job.LastMessage = "Path failed or timed out; skipping target.";
            Save();
            Trace(job, "target_path_failed");
        }
    }

    private static void StartPathToCurrentStand(ActionJob job, NPC npc, Farm farm)
    {
        ActionTile stand = job.RuntimeStandCandidates[job.RuntimeStandCandidateIndex];
        ActionTile target = job.Targets[job.CurrentTargetIndex];
        job.RuntimeStandTile = stand;
        job.RuntimePathStartedTick = Game1.ticks;
        npc.controller = new PathFindController(
            npc,
            farm,
            new Point(stand.X, stand.Y),
            FacingToward(stand, target)
        );
    }

    private void ExecuteCurrentTarget(ActionJob job, NPC npc, Farm farm)
    {
        ActionTile target = job.Targets[job.CurrentTargetIndex];
        Vector2 tile = new(target.X, target.Y);
        bool success = job.Action switch
        {
            ActionIds.WaterCrops => WaterTile(farm, tile),
            ActionIds.ClearWeeds => ClearWeed(farm, tile),
            _ => false
        };
        npc.Sprite.ClearAnimation();
        if (success)
            job.CompletedTargets++;
        else
            job.FailedTargets++;
        job.CurrentTargetIndex++;
        job.RuntimeStandTile = null;
        job.RuntimeStandCandidates.Clear();
        job.State = ActionJobStates.Preparing;
        job.LastMessage = success ? "Target completed and verified." : "Target was no longer eligible.";
        Save();
        Trace(job, success ? "target_completed" : "target_skipped");
    }

    private List<ActionTile> FindTargets(ActionJob job, Farm farm)
    {
        Vector2 anchor = job.AnchorX >= 0
            ? new Vector2(job.AnchorX, job.AnchorY)
            : FindFarmBoundaryEntry(farm);
        IEnumerable<Vector2> candidates = job.Action switch
        {
            ActionIds.WaterCrops => farm.terrainFeatures.Pairs
                .Where(pair =>
                    pair.Value is HoeDirt dirt &&
                    dirt.crop is not null &&
                    !dirt.crop.dead.Value &&
                    dirt.state.Value != HoeDirt.watered
                )
                .Select(pair => pair.Key),
            ActionIds.ClearWeeds => farm.Objects.Pairs
                .Where(pair => IsStrictWeed(pair.Value))
                .Select(pair => pair.Key),
            _ => Enumerable.Empty<Vector2>()
        };
        return candidates
            .Where(tile => TileDistance(tile, anchor) <= config.ActionTargetRadiusTiles || job.AnchorX < 0)
            .OrderBy(tile => TileDistance(tile, anchor))
            .Take(job.MaxTargets)
            .Select(tile => new ActionTile((int)tile.X, (int)tile.Y))
            .ToList();
    }

    private static bool WaterTile(Farm farm, Vector2 tile)
    {
        if (!farm.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature) ||
            feature is not HoeDirt dirt ||
            dirt.crop is null ||
            dirt.crop.dead.Value)
        {
            return false;
        }
        dirt.state.Value = HoeDirt.watered;
        return dirt.state.Value == HoeDirt.watered;
    }

    private static bool ClearWeed(Farm farm, Vector2 tile)
    {
        if (!farm.Objects.TryGetValue(tile, out StardewValley.Object? obj) || !IsStrictWeed(obj))
            return false;
        Tool? scythe = ItemRegistry.Create("(W)47") as Tool;
        bool destroyed = scythe is not null && obj.performToolAction(scythe);
        if (destroyed)
            farm.Objects.Remove(tile);
        return destroyed && !farm.Objects.ContainsKey(tile);
    }

    private static bool IsStrictWeed(StardewValley.Object obj)
    {
        string name = obj.Name ?? "";
        return WeedIds.Contains(obj.QualifiedItemId ?? "") ||
               name.Equals("Weeds", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ActionTile> FindStandTiles(GameLocation location, ActionTile target, Vector2 npcTile)
    {
        ActionTile[] candidates =
        {
            new(target.X, target.Y + 1),
            new(target.X + 1, target.Y),
            new(target.X - 1, target.Y),
            new(target.X, target.Y - 1)
        };
        return candidates
            .Where(tile => IsWalkable(location, tile))
            .OrderBy(tile => TileDistance(new Vector2(tile.X, tile.Y), npcTile))
            .ToList();
    }

    private static bool IsWalkable(GameLocation location, ActionTile tile)
    {
        if (tile.X < 0 || tile.Y < 0 ||
            tile.X >= location.Map.Layers[0].LayerWidth ||
            tile.Y >= location.Map.Layers[0].LayerHeight)
        {
            return false;
        }
        Vector2 vector = new(tile.X, tile.Y);
        if (!location.isTilePassable(new Location(tile.X, tile.Y), Game1.viewport))
            return false;
        if (location.Objects.TryGetValue(vector, out StardewValley.Object? obj) && !obj.isPassable())
            return false;
        if (location.terrainFeatures.TryGetValue(vector, out TerrainFeature? feature) && !feature.isPassable())
            return false;
        return true;
    }

    private static Vector2 FindFarmBoundaryEntry(Farm farm)
    {
        foreach (Warp warp in farm.warps.Where(warp =>
            warp.TargetName.Equals("BusStop", StringComparison.OrdinalIgnoreCase)
        ))
        {
            foreach (ActionTile candidate in InwardWarpTiles(farm, warp))
            {
                if (IsWalkable(farm, candidate))
                    return new Vector2(candidate.X, candidate.Y);
            }
        }

        for (int y = 0; y < farm.Map.Layers[0].LayerHeight; y++)
        {
            for (int x = 0; x < farm.Map.Layers[0].LayerWidth; x++)
            {
                if (IsWalkable(farm, new ActionTile(x, y)))
                    return new Vector2(x, y);
            }
        }
        return new Vector2(64, 15);
    }

    private static Vector2 FindFarmHouseExteriorEntry(Farm farm)
    {
        Point door = farm.GetMainFarmHouseEntry();
        ActionTile[] candidates =
        {
            new(door.X, door.Y + 1),
            new(door.X, door.Y + 2),
            new(door.X, door.Y),
            new(door.X - 1, door.Y + 1),
            new(door.X + 1, door.Y + 1)
        };
        ActionTile? selected = candidates.FirstOrDefault(tile => IsWalkable(farm, tile));
        return selected is null
            ? FindFarmBoundaryEntry(farm)
            : new Vector2(selected.X, selected.Y);
    }

    private static IEnumerable<ActionTile> InwardWarpTiles(Farm farm, Warp warp)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        int[] offsets = { 2, 3, 4, 5, 6, 7, 8, 1 };
        foreach (int offset in offsets)
        {
            if (warp.Y <= 1)
                yield return new ActionTile(warp.X, warp.Y + offset);
            else if (warp.Y >= height - 2)
                yield return new ActionTile(warp.X, warp.Y - offset);
            else if (warp.X <= 1)
                yield return new ActionTile(warp.X + offset, warp.Y);
            else if (warp.X >= width - 2)
                yield return new ActionTile(warp.X - offset, warp.Y);
            else
                yield return new ActionTile(warp.X, warp.Y);
        }
    }

    private void BeginReturn(ActionJob job, NPC npc, Farm farm, string workSummary)
    {
        npc.Sprite.ClearAnimation();
        Vector2 destination = job.ReturnContext.OriginKind switch
        {
            ActionOriginKinds.Farm =>
                new Vector2(job.ReturnContext.TileX, job.ReturnContext.TileY),
            ActionOriginKinds.FarmHouse =>
                FindFarmHouseExteriorEntry(farm),
            _ => FindFarmBoundaryEntry(farm)
        };
        job.RuntimeReturnCandidates = FindReturnCandidates(farm, destination, npc.Tile);
        job.RuntimeReturnCandidateIndex = 0;
        if (job.RuntimeReturnCandidates.Count == 0)
        {
            Fail(job, "No walkable return route endpoint was available.", terminal: false);
            return;
        }

        StartReturnPath(job, npc, farm);
        Game1.addHUDMessage(new HUDMessage(
            job.ReturnContext.OriginKind == ActionOriginKinds.Farm
                ? $"{npc.displayName} finished the work and is returning to where they started."
                : $"{npc.displayName} finished the work and is leaving the farm."
        ));
        Transition(job, ActionJobStates.Returning, workSummary);
    }

    private void UpdateReturn(ActionJob job, NPC npc, Farm farm)
    {
        if (npc.currentLocation is not Farm || job.RuntimeReturnTile is null)
        {
            Complete(job, $"{job.LastMessage} Return route was interrupted.");
            return;
        }

        ActionTile destination = job.RuntimeReturnTile;
        bool arrived =
            (int)npc.Tile.X == destination.X &&
            (int)npc.Tile.Y == destination.Y;
        bool timedOut =
            Game1.ticks - job.RuntimePathStartedTick >
            Math.Max(1800, config.ActionPathTimeoutTicks);
        if (arrived)
        {
            string suffix = job.ReturnContext.OriginKind switch
            {
                ActionOriginKinds.Farm => " NPC returned to the original farm position.",
                ActionOriginKinds.FarmHouse => " NPC reached the farmhouse door.",
                _ => " NPC reached the farm exit."
            };
            Complete(job, job.LastMessage + suffix);
            return;
        }
        if (timedOut)
        {
            Fail(job, "NPC could not complete the return route before timeout.", terminal: false);
            return;
        }
        if (npc.controller is null &&
            Game1.ticks - job.RuntimePathStartedTick >= 15)
        {
            job.RuntimeReturnCandidateIndex++;
            if (job.RuntimeReturnCandidateIndex >= job.RuntimeReturnCandidates.Count)
            {
                Fail(job, "NPC could not find a connected path back to the departure point.", terminal: false);
                return;
            }
            StartReturnPath(job, npc, farm);
            job.LastMessage = "Return path failed; trying another nearby exit tile.";
            Save();
            Trace(job, "return_path_retry");
        }
    }

    private static List<ActionTile> FindReturnCandidates(
        Farm farm,
        Vector2 destination,
        Vector2 npcTile)
    {
        List<ActionTile> candidates = new();
        int centerX = (int)destination.X;
        int centerY = (int)destination.Y;
        for (int radius = 0; radius <= 3; radius++)
        {
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (Math.Abs(x - centerX) + Math.Abs(y - centerY) != radius)
                        continue;
                    ActionTile tile = new(x, y);
                    if (IsWalkable(farm, tile))
                        candidates.Add(tile);
                }
            }
        }
        return candidates
            .DistinctBy(tile => (tile.X, tile.Y))
            .OrderBy(tile => TileDistance(new Vector2(tile.X, tile.Y), destination))
            .ThenBy(tile => TileDistance(new Vector2(tile.X, tile.Y), npcTile))
            .ToList();
    }

    private static void StartReturnPath(ActionJob job, NPC npc, Farm farm)
    {
        ActionTile destination =
            job.RuntimeReturnCandidates[job.RuntimeReturnCandidateIndex];
        job.RuntimeReturnTile = destination;
        job.RuntimePathStartedTick = Game1.ticks;
        npc.controller = new PathFindController(
            npc,
            farm,
            new Point(destination.X, destination.Y),
            2
        );
    }

    private static void StartWorkAnimation(NPC npc)
    {
        int baseFrame = npc.FacingDirection switch
        {
            1 => 4,
            0 => 8,
            3 => 12,
            _ => 0
        };
        npc.Sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
        {
            new(baseFrame + 1, 150),
            new(baseFrame, 100),
            new(baseFrame + 3, 150),
            new(baseFrame + 1, 150),
            new(baseFrame, 100),
            new(baseFrame + 3, 150)
        });
    }

    private static int FacingToward(ActionTile from, ActionTile to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        if (Math.Abs(dx) > Math.Abs(dy))
            return dx > 0 ? 1 : 3;
        return dy > 0 ? 2 : 0;
    }

    private void ReserveNpc(NPC npc)
    {
        npc.controller = null;
        npc.Halt();
        npc.followSchedule = false;
    }

    private void RestoreNpc(ActionJob job)
    {
        NPC? npc = Game1.getCharacterFromName(job.NpcName);
        if (npc is null)
            return;
        npc.controller = null;
        npc.Halt();
        GameLocation? returnLocation = Game1.getLocationFromName(job.ReturnContext.LocationName);
        if (returnLocation is not null)
        {
            Game1.warpCharacter(
                npc,
                returnLocation,
                new Vector2(job.ReturnContext.TileX, job.ReturnContext.TileY)
            );
        }
        npc.FacingDirection = job.ReturnContext.FacingDirection;
        npc.followSchedule = job.ReturnContext.FollowSchedule;
        if (npc.followSchedule)
            npc.checkSchedule(Game1.timeOfDay);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        ActionJob? job = data.Jobs.LastOrDefault(job =>
            job.State == ActionJobStates.Acting &&
            job.Action == ActionIds.WaterCrops
        );
        if (job is null)
            return;
        NPC? npc = Game1.getCharacterFromName(job.NpcName);
        if (npc is null || npc.currentLocation != Game1.currentLocation)
            return;

        ParsedItemData wateringCan = ItemRegistry.GetDataOrErrorItem("(T)WateringCan");
        Texture2D texture = wateringCan.GetTexture();
        Microsoft.Xna.Framework.Rectangle source = wateringCan.GetSourceRect();
        float progress = Math.Clamp(
            (Game1.ticks - job.RuntimeActionStartedTick) / 54f,
            0f,
            1f
        );
        float pour = MathF.Sin(progress * MathF.PI);
        Vector2 worldPosition = npc.Position + (npc.FacingDirection switch
        {
            0 => new Vector2(24, -10),
            1 => new Vector2(48, 24),
            3 => new Vector2(-8, 24),
            _ => new Vector2(30, 48)
        });
        worldPosition.Y -= pour * 8f;
        float baseRotation = npc.FacingDirection switch
        {
            1 => 0.75f,
            3 => -0.75f,
            _ => 0f
        };
        float rotation = baseRotation + npc.FacingDirection switch
        {
            1 => pour * 0.8f,
            3 => -pour * 0.8f,
            _ => pour * 0.55f
        };
        SpriteEffects effects = npc.FacingDirection == 3
            ? SpriteEffects.FlipHorizontally
            : SpriteEffects.None;
        spriteBatch.Draw(
            texture,
            Game1.GlobalToLocal(Game1.viewport, worldPosition),
            source,
            Color.White,
            rotation,
            new Vector2(source.Width / 2f, source.Height / 2f),
            3f,
            effects,
            Math.Min(1f, (npc.StandingPixel.Y + 64) / 10000f)
        );

        if (progress >= 0.35f &&
            job.CurrentTargetIndex < job.Targets.Count)
        {
            DrawWaterDrops(spriteBatch, job, worldPosition, progress, npc);
        }
    }

    private static void DrawWaterDrops(
        SpriteBatch spriteBatch,
        ActionJob job,
        Vector2 canWorldPosition,
        float progress,
        NPC npc)
    {
        ActionTile target = job.Targets[job.CurrentTargetIndex];
        Vector2 start = Game1.GlobalToLocal(
            Game1.viewport,
            canWorldPosition + new Vector2(8, 16)
        );
        Vector2 end = Game1.GlobalToLocal(
            Game1.viewport,
            new Vector2(target.X * 64 + 32, target.Y * 64 + 32)
        );
        float streamProgress = (progress - 0.35f) / 0.65f;
        float depth = Math.Min(1f, (npc.StandingPixel.Y + 65) / 10000f);
        for (int index = 0; index < 4; index++)
        {
            float amount = (streamProgress + index * 0.2f) % 1f;
            Vector2 position = Vector2.Lerp(start, end, amount);
            position.Y -= MathF.Sin(amount * MathF.PI) * 18f;
            Microsoft.Xna.Framework.Rectangle drop = new(
                (int)position.X,
                (int)position.Y,
                5,
                9
            );
            spriteBatch.Draw(
                Game1.staminaRect,
                drop,
                null,
                Color.CornflowerBlue * 0.9f,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                depth
            );
        }
    }

    private void Complete(ActionJob job, string message)
    {
        job.State = ActionJobStates.Completed;
        job.LastMessage = message;
        RestoreNpc(job);
        Save();
        Trace(job, "completed");
        Game1.addHUDMessage(new HUDMessage(
            $"{job.NpcName} finished: {job.CompletedTargets} completed, {job.FailedTargets} skipped."
        ));
    }

    private void Fail(ActionJob job, string message, bool terminal)
    {
        job.State = terminal ? ActionJobStates.FailedTerminal : ActionJobStates.FailedRecoverable;
        job.LastMessage = message;
        RestoreNpc(job);
        Save();
        Trace(job, "failed");
        monitor.Log($"Action job {job.JobId} failed: {message}", LogLevel.Warn);
        Game1.addHUDMessage(new HUDMessage($"{job.NpcName}'s job stopped: {message}"));
    }

    private void Transition(ActionJob job, string state, string message)
    {
        job.State = state;
        job.LastMessage = message;
        Save();
        Trace(job, state);
    }

    private ActionJob? GetActiveJob(string npcName) =>
        data.Jobs.LastOrDefault(job =>
            job.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase) &&
            !ActionJobStates.IsTerminal(job.State)
        );

    private static int GetRequestedMaximum(AgentActionProposal proposal)
    {
        if (proposal.Parameters.TryGetValue("max_targets", out JsonElement value) &&
            value.TryGetInt32(out int maximum))
        {
            return Math.Clamp(maximum, 1, 10);
        }
        return 10;
    }

    private void Save() => helper.Data.WriteSaveData(SaveDataKey, data);

    private void Trace(ActionJob job, string eventName)
    {
        NPC? npc = Game1.getCharacterFromName(job.NpcName);
        ActionTile? currentTarget =
            job.CurrentTargetIndex >= 0 && job.CurrentTargetIndex < job.Targets.Count
                ? job.Targets[job.CurrentTargetIndex]
                : null;
        AppendTrace(new
        {
            timestamp = DateTimeOffset.UtcNow,
            event_name = eventName,
            job_id = job.JobId,
            proposal_id = job.ProposalId,
            save_id = job.SaveId,
            npc_name = job.NpcName,
            action = job.Action,
            origin_kind = job.ReturnContext.OriginKind,
            state = job.State,
            completed_targets = job.CompletedTargets,
            failed_targets = job.FailedTargets,
            target_count = job.Targets.Count,
            current_target = currentTarget,
            dispatch_tile = job.RuntimeDispatchTile,
            return_tile = job.RuntimeReturnTile,
            npc_location = npc?.currentLocation?.NameOrUniqueName,
            npc_tile = npc is null ? null : new { x = (int)npc.Tile.X, y = (int)npc.Tile.Y },
            message = job.LastMessage
        });
    }

    private void AppendTrace(object record)
    {
        try
        {
            string? directory = Path.GetDirectoryName(tracePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(
                tracePath,
                JsonSerializer.Serialize(record) + Environment.NewLine
            );
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not append Action Trace: {ex.Message}", LogLevel.Warn);
        }
    }

    private static int TileDistance(Vector2 left, Vector2 right) =>
        Math.Abs((int)left.X - (int)right.X) + Math.Abs((int)left.Y - (int)right.Y);
}
