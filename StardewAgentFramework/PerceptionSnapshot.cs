using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace StardewAgentFramework;

public sealed class PerceptionSnapshot
{
    public string SchemaVersion { get; init; } = "0.2";
    public TimeContext Time { get; init; } = new();
    public WeatherContext Weather { get; init; } = new();
    public PlayerContext Player { get; init; } = new();
    public LocationContext Location { get; init; } = new();
    public InventoryContext Inventory { get; init; } = new();
    public FarmContext Farm { get; init; } = new();
    public NearbyContext Nearby { get; init; } = new();
    public string[] RiskFlags { get; init; } = Array.Empty<string>();

    public static PerceptionSnapshot FromGame(int nearbyRadius = 8)
    {
        Farmer player = Game1.player;
        GameLocation? location = Game1.currentLocation;
        InventoryContext inventory = InventoryContext.FromPlayer(player);
        FarmContext farm = FarmContext.FromFarm();
        NearbyContext nearby = NearbyContext.FromLocation(location, player, nearbyRadius);

        return new PerceptionSnapshot
        {
            Time = TimeContext.FromGame(),
            Weather = WeatherContext.FromGame(),
            Player = PlayerContext.FromPlayer(player),
            Location = LocationContext.FromLocation(location),
            Inventory = inventory,
            Farm = farm,
            Nearby = nearby,
            RiskFlags = BuildRiskFlags(player, inventory, nearby)
        };
    }

    public string ToSummary()
    {
        string flags = RiskFlags.Length == 0 ? "none" : string.Join(", ", RiskFlags);
        return
            $"Player={Player.Name}, Farm={Player.FarmName}, Location={Location.Name}, " +
            $"Date={Time.Season} {Time.DayOfMonth} Y{Time.Year}, Time={Time.TimeOfDay}, Weather={Weather.Weather}, " +
            $"Money={Player.Money}, Energy={Player.Energy}/{Player.MaxEnergy}, " +
            $"Inventory={Inventory.FilledSlots}/{Inventory.Capacity}, " +
            $"FarmCrops(water={Farm.CropsNeedWatering}, mature={Farm.MatureCrops}, dead={Farm.DeadCrops}), " +
            $"Nearby(npcs={Nearby.Npcs.Length}, objects={Nearby.Objects.Length}, cropsWater={Nearby.CropsNeedWatering}), " +
            $"Risks=[{flags}]";
    }

    private static string[] BuildRiskFlags(Farmer player, InventoryContext inventory, NearbyContext nearby)
    {
        List<string> flags = new();
        if (player.Stamina < Math.Max(25, player.MaxStamina * 0.15f))
            flags.Add("low_energy");
        if (Game1.timeOfDay >= 2200)
            flags.Add("late_night");
        if (inventory.EmptySlots <= 2)
            flags.Add("inventory_nearly_full");
        if (nearby.Monsters.Length > 0)
            flags.Add("hostile_nearby");
        if (Game1.isRaining)
            flags.Add("rain_no_outdoor_watering_needed");
        return flags.ToArray();
    }
}

public sealed class TimeContext
{
    public string Season { get; init; } = "";
    public int DayOfMonth { get; init; }
    public int Year { get; init; }
    public int TimeOfDay { get; init; }
    public string DayOfWeek { get; init; } = "";
    public int MinutesUntilPassOut { get; init; }

    public static TimeContext FromGame()
    {
        return new TimeContext
        {
            Season = Game1.currentSeason,
            DayOfMonth = Game1.dayOfMonth,
            Year = Game1.year,
            TimeOfDay = Game1.timeOfDay,
            DayOfWeek = GetDayOfWeek(Game1.dayOfMonth),
            MinutesUntilPassOut = MinutesBetween(Game1.timeOfDay, 2600)
        };
    }

    private static string GetDayOfWeek(int day)
    {
        string[] names = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        return names[Math.Max(0, day - 1) % names.Length];
    }

    private static int MinutesBetween(int current, int target)
    {
        int currentHours = current / 100;
        int currentMinutes = current % 100;
        int targetHours = target / 100;
        int targetMinutes = target % 100;
        return Math.Max(0, (targetHours * 60 + targetMinutes) - (currentHours * 60 + currentMinutes));
    }
}

public sealed class WeatherContext
{
    public string Weather { get; init; } = "";
    public bool IsRaining { get; init; }
    public bool IsLightning { get; init; }
    public bool IsSnowing { get; init; }
    public bool IsDebrisWeather { get; init; }

    public static WeatherContext FromGame()
    {
        string weather = Game1.isRaining ? (Game1.isLightning ? "storm" : "rain") :
            Game1.isSnowing ? "snow" :
            Game1.isDebrisWeather ? "wind" : "sunny";

        return new WeatherContext
        {
            Weather = weather,
            IsRaining = Game1.isRaining,
            IsLightning = Game1.isLightning,
            IsSnowing = Game1.isSnowing,
            IsDebrisWeather = Game1.isDebrisWeather
        };
    }
}

public sealed class PlayerContext
{
    public string Name { get; init; } = "";
    public string FarmName { get; init; } = "";
    public int TileX { get; init; }
    public int TileY { get; init; }
    public int FacingDirection { get; init; }
    public string FacingDirectionName { get; init; } = "";
    public int Money { get; init; }
    public int Energy { get; init; }
    public int MaxEnergy { get; init; }
    public int Health { get; init; }
    public int MaxHealth { get; init; }
    public string CurrentTool { get; init; } = "";

    public static PlayerContext FromPlayer(Farmer player)
    {
        return new PlayerContext
        {
            Name = player.Name,
            FarmName = player.farmName.Value,
            TileX = (int)player.Tile.X,
            TileY = (int)player.Tile.Y,
            FacingDirection = player.FacingDirection,
            FacingDirectionName = DirectionName(player.FacingDirection),
            Money = player.Money,
            Energy = (int)player.Stamina,
            MaxEnergy = player.MaxStamina,
            Health = player.health,
            MaxHealth = player.maxHealth,
            CurrentTool = player.CurrentTool?.Name ?? "None"
        };
    }

    private static string DirectionName(int direction)
    {
        return direction switch
        {
            0 => "up",
            1 => "right",
            2 => "down",
            3 => "left",
            _ => "unknown"
        };
    }
}

public sealed class LocationContext
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsOutdoors { get; init; }
    public bool IsFarm { get; init; }
    public bool IsFarmHouse { get; init; }
    public bool IsMine { get; init; }
    public string Type { get; init; } = "";

    public static LocationContext FromLocation(GameLocation? location)
    {
        return new LocationContext
        {
            Name = location?.NameOrUniqueName ?? "",
            DisplayName = location?.DisplayName ?? location?.Name ?? "",
            IsOutdoors = location?.IsOutdoors ?? false,
            IsFarm = location is Farm,
            IsFarmHouse = location is FarmHouse,
            IsMine = location is MineShaft,
            Type = LocationType(location)
        };
    }

    private static string LocationType(GameLocation? location)
    {
        if (location is null) return "unknown";
        if (location is Farm) return "farm";
        if (location is FarmHouse) return "farmhouse";
        if (location is MineShaft) return "mine";
        if (location is Town) return "town";
        if (location is Beach) return "beach";
        if (location is Forest) return "forest";
        if (location is Mountain) return "mountain";
        return "other";
    }
}

public sealed class InventoryContext
{
    public int Capacity { get; init; }
    public int FilledSlots { get; init; }
    public int EmptySlots { get; init; }
    public InventoryItemInfo[] Items { get; init; } = Array.Empty<InventoryItemInfo>();
    public string[] Tools { get; init; } = Array.Empty<string>();
    public string[] Food { get; init; } = Array.Empty<string>();
    public string[] Seeds { get; init; } = Array.Empty<string>();

    public static InventoryContext FromPlayer(Farmer player)
    {
        InventoryItemInfo[] items = player.Items
            .Select((item, index) => item is null ? null : new InventoryItemInfo
            {
                Slot = index,
                Name = item.Name,
                DisplayName = item.DisplayName,
                Stack = item.Stack,
                Category = item.getCategoryName(),
                IsTool = item is Tool,
                IsFood = item is SObject obj && obj.Edibility > -300,
                IsSeed = item.Name.Contains("Seeds", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Starter", StringComparison.OrdinalIgnoreCase)
            })
            .Where(item => item is not null)
            .Cast<InventoryItemInfo>()
            .ToArray();

        return new InventoryContext
        {
            Capacity = player.Items.Count,
            FilledSlots = items.Length,
            EmptySlots = Math.Max(0, player.Items.Count - items.Length),
            Items = items.Take(24).ToArray(),
            Tools = items.Where(item => item.IsTool).Select(item => item.DisplayName).ToArray(),
            Food = items.Where(item => item.IsFood).Select(item => item.DisplayName).Take(8).ToArray(),
            Seeds = items.Where(item => item.IsSeed).Select(item => item.DisplayName).Take(8).ToArray()
        };
    }
}

public sealed class InventoryItemInfo
{
    public int Slot { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int Stack { get; init; }
    public string Category { get; init; } = "";
    public bool IsTool { get; init; }
    public bool IsFood { get; init; }
    public bool IsSeed { get; init; }
}

public sealed class FarmContext
{
    public int TotalCrops { get; init; }
    public int CropsNeedWatering { get; init; }
    public int MatureCrops { get; init; }
    public int DeadCrops { get; init; }
    public int FarmObjectsReady { get; init; }
    public int Animals { get; init; }
    public int AnimalsNeedPetting { get; init; }

    public static FarmContext FromFarm()
    {
        Farm farm = Game1.getFarm();
        List<HoeDirt> cropTiles = farm.terrainFeatures.Values
            .OfType<HoeDirt>()
            .Where(dirt => dirt.crop is not null)
            .ToList();

        return new FarmContext
        {
            TotalCrops = cropTiles.Count,
            CropsNeedWatering = cropTiles.Count(NeedsWatering),
            MatureCrops = cropTiles.Count(dirt => dirt.crop?.fullyGrown.Value == true),
            DeadCrops = cropTiles.Count(dirt => dirt.crop?.dead.Value == true),
            FarmObjectsReady = farm.Objects.Values.Count(IsObjectReady),
            Animals = farm.animals?.Count() ?? 0,
            AnimalsNeedPetting = farm.animals?.Values.Count(animal => !animal.wasPet.Value) ?? 0
        };
    }

    private static bool NeedsWatering(HoeDirt dirt)
    {
        return dirt.crop is not null && dirt.state.Value == 0 && dirt.crop.dead.Value == false;
    }

    private static bool IsObjectReady(SObject obj)
    {
        return obj.readyForHarvest.Value || obj.heldObject.Value is not null;
    }
}

public sealed class NearbyContext
{
    public int Radius { get; init; }
    public NearbyNpcInfo[] Npcs { get; init; } = Array.Empty<NearbyNpcInfo>();
    public NearbyObjectInfo[] Objects { get; init; } = Array.Empty<NearbyObjectInfo>();
    public int CropsNeedWatering { get; init; }
    public int MatureCrops { get; init; }
    public string[] Monsters { get; init; } = Array.Empty<string>();
    public TileInfo TileInFront { get; init; } = new();

    public static NearbyContext FromLocation(GameLocation? location, Farmer player, int radius)
    {
        if (location is null)
            return new NearbyContext { Radius = radius };

        int playerX = (int)player.Tile.X;
        int playerY = (int)player.Tile.Y;

        List<HoeDirt> nearbyCropTiles = location.terrainFeatures.Pairs
            .Where(pair => Within(pair.Key, playerX, playerY, radius))
            .Select(pair => pair.Value)
            .OfType<HoeDirt>()
            .Where(dirt => dirt.crop is not null)
            .ToList();

        return new NearbyContext
        {
            Radius = radius,
            Npcs = location.characters
                .Where(npc => npc is not null && !npc.IsMonster && Within(npc.Tile, playerX, playerY, radius))
                .Select(npc => new NearbyNpcInfo
                {
                    Name = npc.Name,
                    DisplayName = npc.displayName,
                    TileX = (int)npc.Tile.X,
                    TileY = (int)npc.Tile.Y,
                    Distance = Chebyshev(npc.Tile, playerX, playerY),
                    CanSocialize = npc.CanSocialize
                })
                .OrderBy(npc => npc.Distance)
                .Take(12)
                .ToArray(),
            Objects = location.Objects.Pairs
                .Where(pair => Within(pair.Key, playerX, playerY, radius))
                .Select(pair => new NearbyObjectInfo
                {
                    Name = pair.Value.Name,
                    DisplayName = pair.Value.DisplayName,
                    TileX = (int)pair.Key.X,
                    TileY = (int)pair.Key.Y,
                    IsReady = IsObjectReady(pair.Value),
                    IsPassable = pair.Value.isPassable()
                })
                .OrderBy(obj => Math.Max(Math.Abs(obj.TileX - playerX), Math.Abs(obj.TileY - playerY)))
                .Take(16)
                .ToArray(),
            CropsNeedWatering = nearbyCropTiles.Count(FarmContextNeedsWatering),
            MatureCrops = nearbyCropTiles.Count(dirt => dirt.crop?.fullyGrown.Value == true),
            Monsters = location.characters
                .Where(npc => npc is not null && npc.IsMonster && Within(npc.Tile, playerX, playerY, radius))
                .Select(npc => npc.Name)
                .Take(8)
                .ToArray(),
            TileInFront = TileInfo.FromTileInFront(location, player)
        };
    }

    private static bool FarmContextNeedsWatering(HoeDirt dirt)
    {
        return dirt.crop is not null && dirt.state.Value == 0 && dirt.crop.dead.Value == false;
    }

    private static bool IsObjectReady(SObject obj)
    {
        return obj.readyForHarvest.Value || obj.heldObject.Value is not null;
    }

    private static bool Within(Vector2 tile, int centerX, int centerY, int radius)
    {
        return Math.Abs((int)tile.X - centerX) <= radius && Math.Abs((int)tile.Y - centerY) <= radius;
    }

    private static int Chebyshev(Vector2 tile, int centerX, int centerY)
    {
        return Math.Max(Math.Abs((int)tile.X - centerX), Math.Abs((int)tile.Y - centerY));
    }
}

public sealed class NearbyNpcInfo
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int TileX { get; init; }
    public int TileY { get; init; }
    public int Distance { get; init; }
    public bool CanSocialize { get; init; }
}

public sealed class NearbyObjectInfo
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int TileX { get; init; }
    public int TileY { get; init; }
    public bool IsReady { get; init; }
    public bool IsPassable { get; init; }
}

public sealed class TileInfo
{
    public int TileX { get; init; }
    public int TileY { get; init; }
    public bool IsPassable { get; init; }
    public bool IsWater { get; init; }
    public string? ObjectName { get; init; }
    public string? TerrainType { get; init; }
    public string? NpcName { get; init; }

    public static TileInfo FromTileInFront(GameLocation location, Farmer player)
    {
        int tileX = (int)player.Tile.X;
        int tileY = (int)player.Tile.Y;
        switch (player.FacingDirection)
        {
            case 0: tileY--; break;
            case 1: tileX++; break;
            case 2: tileY++; break;
            case 3: tileX--; break;
        }

        Vector2 tile = new(tileX, tileY);
        location.Objects.TryGetValue(tile, out SObject? obj);
        location.terrainFeatures.TryGetValue(tile, out TerrainFeature? terrain);
        NPC? npc = location.characters.FirstOrDefault(character => (int)character.Tile.X == tileX && (int)character.Tile.Y == tileY);

        return new TileInfo
        {
            TileX = tileX,
            TileY = tileY,
            IsPassable = location.isTilePassable(new xTile.Dimensions.Location(tileX, tileY), Game1.viewport),
            IsWater = location.isWaterTile(tileX, tileY),
            ObjectName = obj?.DisplayName,
            TerrainType = terrain?.GetType().Name,
            NpcName = npc?.displayName
        };
    }
}
