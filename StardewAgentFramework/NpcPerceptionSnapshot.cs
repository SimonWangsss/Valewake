using System;
using System.Linq;
using StardewValley;

namespace StardewAgentFramework;

public sealed class NpcPerceptionSnapshot
{
    public string SchemaVersion { get; init; } = "npc-perception-0.1";
    public TimeContext Time { get; init; } = new();
    public WeatherContext Weather { get; init; } = new();
    public LocationContext Location { get; init; } = new();
    public VisiblePlayerContext Player { get; init; } = new();
    public VisibleNearbyContext Nearby { get; init; } = new();

    public static NpcPerceptionSnapshot FromGame(NPC npc, int radius = 6)
    {
        Farmer player = Game1.player;
        NearbyContext nearby = NearbyContext.FromLocation(Game1.currentLocation, player, radius);
        return new NpcPerceptionSnapshot
        {
            Time = TimeContext.FromGame(),
            Weather = WeatherContext.FromGame(),
            Location = LocationContext.FromLocation(Game1.currentLocation),
            Player = VisiblePlayerContext.FromPlayer(player, npc),
            Nearby = new VisibleNearbyContext
            {
                Radius = radius,
                Npcs = nearby.Npcs.Select(item => item.DisplayName).ToArray(),
                Objects = nearby.Objects.Select(item => item.DisplayName).Distinct().Take(12).ToArray(),
                CropsNeedWatering = nearby.CropsNeedWatering,
                MatureCrops = nearby.MatureCrops,
                Monsters = nearby.Monsters
            }
        };
    }
}

public sealed class VisiblePlayerContext
{
    public string Name { get; init; } = "";
    public string HeldItem { get; init; } = "None";
    public int FriendshipPoints { get; init; }
    public int Hearts { get; init; }
    public string RelationshipStatus { get; init; } = "acquaintance";

    public static VisiblePlayerContext FromPlayer(Farmer player, NPC npc)
    {
        player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship);
        int points = friendship?.Points ?? 0;
        return new VisiblePlayerContext
        {
            Name = player.Name,
            HeldItem = player.CurrentItem?.DisplayName ?? "None",
            FriendshipPoints = points,
            Hearts = points / 250,
            RelationshipStatus = GetRelationshipStatus(friendship)
        };
    }

    private static string GetRelationshipStatus(Friendship? friendship)
    {
        if (friendship is null) return "acquaintance";
        if (friendship.IsMarried()) return "married";
        if (friendship.IsEngaged()) return "engaged";
        if (friendship.IsDating()) return "dating";
        if (friendship.IsDivorced()) return "divorced";
        return "friends";
    }
}

public sealed class VisibleNearbyContext
{
    public int Radius { get; init; }
    public string[] Npcs { get; init; } = Array.Empty<string>();
    public string[] Objects { get; init; } = Array.Empty<string>();
    public int CropsNeedWatering { get; init; }
    public int MatureCrops { get; init; }
    public string[] Monsters { get; init; } = Array.Empty<string>();
}
