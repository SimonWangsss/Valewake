using System.Reflection;
namespace StardewValley
{
    public sealed class Farmer { public string Name = "Tester"; public Dictionary<string, string> modData = new(); }
    public sealed class Date { public int TotalDays = 142; }
    public readonly record struct TilePoint(int X, int Y);
    public sealed class NPC { public string Name = "Leah"; public TilePoint Tile = new(4, 4); public bool IsInvisible; }
    public sealed class Response { public string responseKey = ""; public string responseText = ""; }
    public sealed class Dialogue { public NPC? speaker; public void chooseResponse(Response response) { } }
    public sealed class Event { public string id = "leah_story"; public List<NPC> actors = new(); public void answerDialogue(string key, int choice) { } }
    public sealed class Layer { public int LayerWidth = 100, LayerHeight = 100; }
    public sealed class Map { public Layer[] Layers = { new() }; }
    public sealed class Location { public Map Map = new(); }
    public static class Game1
    {
        public static Farmer player = new(); public static Date Date = new(); public static int timeOfDay = 1000;
        public static Event? CurrentEvent; public static object? activeClickableMenu; public static Location currentLocation = new();
    }
}
namespace StardewValley.Menus
{
    public sealed class DialogueBox
    {
        public StardewValley.Dialogue? characterDialogue;
        public StardewValley.Response[] responses = Array.Empty<StardewValley.Response>();
        public string Text = "";
        public string FutureBranch = "NEVER DISPLAYED";
        public string getCurrentString() => Text;
    }
}
namespace StardewModdingAPI
{
    public static class Context { public static bool IsWorldReady = true; }
    public enum LogLevel { Warn }
    public interface IMonitor { void Log(string text, LogLevel level); }
}
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodInfo original, HarmonyMethod prefix) { if (original is null) throw new Exception("Missing hook."); }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools { public static MethodInfo Method(Type type, string name, Type[]? types = null) => types is null ? type.GetMethod(name)! : type.GetMethod(name, types)!; }
}
