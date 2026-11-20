using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace Valewake;

internal static class NpcInteractionJournal
{
    private const string Key = "Valewake/NpcInteractionJournal";
    private static Action<GiftEntry>? onRecorded;
    private static IMonitor? monitor;

    public sealed class GiftEntry
    {
        public string event_id { get; set; } = "";
        public string npc { get; set; } = "";
        public string kind { get; set; } = "gift_received";
        public string source { get; set; } = "game_event";
        public int game_day { get; set; }
        public int game_time { get; set; }
        public string item_id { get; set; } = "";
        public string item_name { get; set; } = "";
        public string gift_taste { get; set; } = "";
    }

    private static List<GiftEntry> Read(Farmer player)
    {
        if (!player.modData.TryGetValue(Key, out string? raw)) return new();
        try { return JsonSerializer.Deserialize<List<GiftEntry>>(raw) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static GiftEntry[] ForNpc(Farmer player, string npc) => Read(player)
        .Where(e => e.npc == npc).TakeLast(8).ToArray();

    public static void Install(string id, IMonitor log, Action<GiftEntry> callback)
    {
        monitor = log;
        onRecorded = callback;
        new Harmony(id).Patch(AccessTools.Method(typeof(NPC), nameof(NPC.receiveGift)),
            prefix: new HarmonyMethod(typeof(NpcInteractionJournal), nameof(BeforeGift)),
            postfix: new HarmonyMethod(typeof(NpcInteractionJournal), nameof(AfterGift)));
    }

    private static void BeforeGift(out uint __state) => __state = Game1.stats.GiftsGiven;

    private static void AfterGift(NPC __instance, StardewValley.Object o, Farmer giver, uint __state)
    {
        if (!Context.IsWorldReady || Game1.stats.GiftsGiven <= __state) return;
        try
        {
            GiftEntry entry = new() { event_id = $"gift_{Guid.NewGuid():N}", npc = __instance.Name,
                game_day = Game1.Date.TotalDays, game_time = Game1.timeOfDay,
                item_id = o.QualifiedItemId, item_name = o.DisplayName,
                gift_taste = __instance.getGiftTasteForThisItem(o) switch
                { 0 => "love", 2 => "like", 4 => "dislike", 6 => "hate", _ => "neutral" } };
            List<GiftEntry> entries = Read(giver);
            entries.Add(entry);
            giver.modData[Key] = JsonSerializer.Serialize(entries.TakeLast(256));
            if (giver == Game1.player) onRecorded?.Invoke(entry);
        }
        catch (Exception ex) { monitor?.Log($"Could not record received gift: {ex.Message}", LogLevel.Warn); }
    }
}
