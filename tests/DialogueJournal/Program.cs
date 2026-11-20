using System;
using System.Collections.Generic;
using Valewake;
using StardewValley;

internal static class Program
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    public static void Main()
    {
        Check(DailyDialogueJournal.ForNpc("Haley").Count == 0, "empty day");
        DailyDialogueJournal.Append("Haley", 910, "你好\n今天开心吗？", "很开心。");
        Check(DailyDialogueJournal.ForNpc("Haley")[0].PlayerText.Contains("\n"), "Unicode and multiline JSON roundtrip");
        Check(DailyDialogueJournal.ForNpc("Abigail").Count == 0, "NPC isolation");
        Farmer saved = new() { modData = new Dictionary<string, string>(Game1.player.modData) };
        DailyDialogueJournal.Append("Haley", 920, "unsaved", "reply");
        Game1.player = saved;
        Check(DailyDialogueJournal.ForNpc("Haley").Count == 1, "save rollback via player modData");
        Game1.Date.TotalDays++;
        Check(DailyDialogueJournal.ForNpc("Haley").Count == 0, "new day excludes yesterday");
        DailyDialogueJournal.Append("Haley", 600, "new day", "reply");
        Check(System.Text.Json.JsonDocument.Parse(Game1.player.modData["Valewake/DailyDialogueJournal"]).RootElement.GetArrayLength() == 1,
            "old entries pruned on write");
        Game1.player = new Farmer();
        Check(DailyDialogueJournal.ForNpc("Haley").Count == 0, "player/save isolation");
        Game1.player.modData["Valewake/DailyDialogueJournal"] = "invalid";
        Check(DailyDialogueJournal.ForNpc("Haley").Count == 0, "corrupt JSON fallback");
    }
}

namespace StardewValley
{
    public sealed class Farmer { public Dictionary<string, string> modData = new(); }
    public sealed class Date { public int TotalDays = 1; }
    public static class Game1
    {
        public static Farmer player = new();
        public static Date Date = new();
    }
}
