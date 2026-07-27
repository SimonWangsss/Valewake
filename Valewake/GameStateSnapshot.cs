using System;
using System.Linq;
using StardewValley;

namespace Valewake;

public sealed class GameStateSnapshot
{
    public string FarmerName { get; init; } = "";
    public string FarmName { get; init; } = "";
    public string LocationName { get; init; } = "";
    public string Season { get; init; } = "";
    public int DayOfMonth { get; init; }
    public int TimeOfDay { get; init; }
    public bool IsRaining { get; init; }
    public int Money { get; init; }
    public int Stamina { get; init; }
    public string[] InventoryItems { get; init; } = Array.Empty<string>();

    public static GameStateSnapshot FromGame()
    {
        Farmer player = Game1.player;
        return new GameStateSnapshot
        {
            FarmerName = player?.Name ?? "",
            FarmName = player?.farmName.Value ?? "",
            LocationName = Game1.currentLocation?.NameOrUniqueName ?? "",
            Season = Game1.currentSeason,
            DayOfMonth = Game1.dayOfMonth,
            TimeOfDay = Game1.timeOfDay,
            IsRaining = Game1.isRaining,
            Money = player?.Money ?? 0,
            Stamina = player is null ? 0 : (int)player.Stamina,
            InventoryItems = player?.Items
                .Where(item => item is not null)
                .Select(item => $"{item!.Name} x{item.Stack}")
                .Take(12)
                .ToArray() ?? Array.Empty<string>()
        };
    }

    public string ToSummary()
    {
        string inventory = InventoryItems.Length == 0
            ? "empty"
            : string.Join(", ", InventoryItems);

        return
            $"Farmer={FarmerName}, Farm={FarmName}, Location={LocationName}, " +
            $"Date={Season} {DayOfMonth}, Time={TimeOfDay}, Raining={IsRaining}, " +
            $"Money={Money}, Stamina={Stamina}, Inventory=[{inventory}]";
    }
}
