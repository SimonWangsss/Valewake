using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StardewValley;

namespace Valewake;

/// <summary>Player-owned save data, separate from model context and diagnostic traces.</summary>
internal static class DailyDialogueJournal
{
    private const string Key = "Valewake/DailyDialogueJournal";

    public sealed class Entry
    {
        public string Npc { get; set; } = "";
        public int Day { get; set; }
        public int Time { get; set; }
        public string PlayerText { get; set; } = "";
        public string Reply { get; set; } = "";
    }

    private static List<Entry> Read()
    {
        if (!Game1.player.modData.TryGetValue(Key, out string? raw) || string.IsNullOrWhiteSpace(raw))
            return new();
        try { return JsonSerializer.Deserialize<List<Entry>>(raw) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static IReadOnlyList<Entry> ForNpc(string npc) => Read()
        .Where(e => e.Day == Game1.Date.TotalDays && e.Npc == npc).ToList();

    public static void Append(string npc, int time, string input, string reply)
    {
        List<Entry> entries = Read().Where(e => e.Day == Game1.Date.TotalDays).ToList();
        entries.Add(new Entry { Npc = npc, Day = Game1.Date.TotalDays, Time = time,
            PlayerText = input, Reply = reply });
        Game1.player.modData[Key] = JsonSerializer.Serialize(entries);
    }
}
