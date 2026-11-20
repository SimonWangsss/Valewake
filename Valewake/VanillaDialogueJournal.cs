using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StardewValley;

namespace Valewake;

internal static class VanillaDialogueJournal
{
    private const string Key = "Valewake/ObservedVanillaDialogue";
    public sealed class Entry
    {
        public string event_id { get; set; } = "";
        public string npc { get; set; } = "";
        public string speaker { get; set; } = "";
        public string kind { get; set; } = "vanilla_dialogue";
        public string source { get; set; } = "observed_game_dialogue";
        public string story_event_id { get; set; } = "";
        public string text { get; set; } = "";
        public int game_day { get; set; }
        public int game_time { get; set; }
    }

    private static List<Entry> Read(Farmer player)
    {
        if (!player.modData.TryGetValue(Key, out string? raw)) return new();
        try { return JsonSerializer.Deserialize<List<Entry>>(raw) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static Entry[] ForNpc(Farmer player, string npc) => Read(player).Where(e => e.npc == npc).TakeLast(16).ToArray();

    public static Entry[] Append(Farmer player, IEnumerable<string> participants, string speaker, string text, string storyId, bool choice = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<Entry>();
        string id = $"vanilla_{Guid.NewGuid():N}";
        Entry[] added = participants.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(24).Select(n => new Entry
        {
            event_id = id, npc = n, speaker = speaker, text = text[..Math.Min(text.Length, 800)],
            story_event_id = storyId, game_day = Game1.Date.TotalDays, game_time = Game1.timeOfDay,
            kind = choice ? "vanilla_choice" : "vanilla_dialogue"
        }).ToArray();
        if (added.Length > 0) player.modData[Key] = JsonSerializer.Serialize(Read(player).Concat(added).TakeLast(512));
        return added;
    }
}
