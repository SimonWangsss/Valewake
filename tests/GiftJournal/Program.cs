using System;
using System.Collections.Generic;
using System.Reflection;
using Valewake;
using StardewValley;

internal static class Program
{
    private static int passed;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    public static void Main()
    {
        NpcInteractionJournal.Install("test", new FakeMonitor(), _ => { });
        MethodInfo after = typeof(NpcInteractionJournal).GetMethod("AfterGift", BindingFlags.NonPublic | BindingFlags.Static)!;
        NPC npc = new() { Name = "Penny" };
        StardewValley.Object coffee = new();
        void Gift(Farmer player, bool received = true)
        {
            uint before = Game1.stats.GiftsGiven;
            if (received) Game1.stats.GiftsGiven++;
            after.Invoke(null, new object[] { npc, coffee, player, before });
        }
        Gift(Game1.player, false);
        Check(NpcInteractionJournal.ForNpc(Game1.player, "Penny").Length == 0, "rejected gift not recorded");
        Gift(Game1.player);
        var gift = NpcInteractionJournal.ForNpc(Game1.player, "Penny")[0];
        Check(gift.item_name == "咖啡" && gift.item_id == "(O)395", "actual item captured");
        Check(gift.game_day == 141 && gift.game_time == 1910 && gift.gift_taste == "dislike", "date and reaction captured");
        Check(Game1.stats.GiftsGiven == 1, "journal does not change vanilla gift count");
        Check(NpcInteractionJournal.ForNpc(Game1.player, "Sam").Length == 0, "NPC scope");
        Check(NpcInteractionJournal.ForNpc(new Farmer(), "Penny").Length == 0, "player scope");
        Farmer checkpoint = new() { modData = new(Game1.player.modData) };
        Gift(Game1.player);
        Game1.player = checkpoint;
        Check(NpcInteractionJournal.ForNpc(Game1.player, "Penny").Length == 1, "unsaved gift rolls back with player data");
        for (int i = 0; i < 270; i++) Gift(Game1.player);
        Check(NpcInteractionJournal.ForNpc(Game1.player, "Penny").Length == 8, "bounded per-NPC context");
        Console.WriteLine($"Gift journal: {passed}/{passed} (game/Harmony substitutes, not live interception).");
    }

    private sealed class FakeMonitor : StardewModdingAPI.IMonitor
    {
        public void Log(string message, StardewModdingAPI.LogLevel level) => throw new Exception(message);
    }
}

namespace StardewValley
{
    public sealed class Farmer { public Dictionary<string, string> modData = new(); }
    public sealed class Object { public string QualifiedItemId = "(O)395"; public string DisplayName = "咖啡"; }
    public sealed class NPC
    {
        public string Name = "";
        public void receiveGift() { }
        public int getGiftTasteForThisItem(Object o) => 4;
    }
    public sealed class Date { public int TotalDays = 141; }
    public sealed class Stats { public uint GiftsGiven; }
    public static class Game1
    {
        public static Farmer player = new();
        public static Date Date = new();
        public static Stats stats = new();
        public static int timeOfDay = 1910;
    }
}
namespace StardewModdingAPI
{
    public static class Context { public static bool IsWorldReady = true; }
    public enum LogLevel { Warn }
    public interface IMonitor { void Log(string message, LogLevel level); }
}
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodInfo original, HarmonyMethod prefix, HarmonyMethod postfix) { }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools { public static MethodInfo Method(Type type, string name) => type.GetMethod(name)!; }
}
