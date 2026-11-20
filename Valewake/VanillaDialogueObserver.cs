using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Valewake;

internal static class VanillaDialogueObserver
{
    private sealed class PageState { public bool Generated; public string Last = ""; }
    private static ConditionalWeakTable<DialogueBox, PageState> pages = new();
    private static Action<VanillaDialogueJournal.Entry>? recorded;
    private static IMonitor? monitor;
    private static bool warned;

    public static void Install(string id, IMonitor log, Action<VanillaDialogueJournal.Entry> callback)
    {
        monitor = log;
        recorded = callback;
        Harmony harmony = new(id);
        harmony.Patch(AccessTools.Method(typeof(Event), "answerDialogue", new[] { typeof(string), typeof(int) }),
            prefix: new HarmonyMethod(typeof(VanillaDialogueObserver), nameof(BeforeStoryChoice)));
        harmony.Patch(AccessTools.Method(typeof(Dialogue), nameof(Dialogue.chooseResponse)),
            prefix: new HarmonyMethod(typeof(VanillaDialogueObserver), nameof(BeforeNpcChoice)));
    }

    public static void Reset() { pages = new(); warned = false; }
    private static PageState Page(DialogueBox box) => pages.GetValue(box, _ => new PageState());
    public static void MarkGenerated(DialogueBox box) => Page(box).Generated = true;

    private static string[] Participants(string? speaker) => Game1.CurrentEvent is Event story
        ? story.actors.Where(a => !a.IsInvisible && a.Tile.X >= 0 && a.Tile.Y >= 0 &&
                a.Tile.X < Game1.currentLocation.Map.Layers[0].LayerWidth && a.Tile.Y < Game1.currentLocation.Map.Layers[0].LayerHeight)
            .Select(a => a.Name).Append(speaker ?? "").Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray()
        : string.IsNullOrWhiteSpace(speaker) ? Array.Empty<string>() : new[] { speaker! };

    public static void Observe(DialogueBox? box)
    {
        if (!Context.IsWorldReady || box is null) return;
        try
        {
            PageState page = Page(box);
            if (page.Generated) return;
            string speaker = box.characterDialogue?.speaker?.Name ?? "";
            if (Game1.CurrentEvent is null && speaker.Length == 0) return;
            string text = box.getCurrentString();
            string key = $"{Game1.CurrentEvent?.id}:{speaker}:{text}";
            if (page.Last == key || string.IsNullOrWhiteSpace(text)) return;
            page.Last = key;
            Store(Participants(speaker), speaker.Length == 0 ? "narrator" : speaker, text, false);
        }
        catch (Exception ex) { WarnOnce(ex); }
    }

    private static void BeforeStoryChoice(Event __instance, int answerChoice)
    {
        if (!Context.IsWorldReady || __instance != Game1.CurrentEvent || Game1.activeClickableMenu is not DialogueBox box) return;
        try
        {
            if (Page(box).Generated || answerChoice < 0 || answerChoice >= box.responses.Length) return;
            Observe(box);
            Store(Participants(box.characterDialogue?.speaker?.Name), Game1.player.Name, box.responses[answerChoice].responseText, true);
        }
        catch (Exception ex) { WarnOnce(ex); }
    }

    private static void BeforeNpcChoice(Dialogue __instance, Response response)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not DialogueBox box || box.characterDialogue != __instance) return;
        try
        {
            if (Page(box).Generated || !box.responses.Any(r => r.responseKey == response.responseKey)) return;
            Observe(box);
            Store(Participants(__instance.speaker?.Name), Game1.player.Name, response.responseText, true);
        }
        catch (Exception ex) { WarnOnce(ex); }
    }

    private static void Store(string[] participants, string speaker, string text, bool choice)
    {
        foreach (var entry in VanillaDialogueJournal.Append(Game1.player, participants, speaker, text, Game1.CurrentEvent?.id ?? "", choice))
            recorded?.Invoke(entry);
    }

    private static void WarnOnce(Exception ex)
    {
        if (warned) return;
        warned = true;
        monitor?.Log($"Could not observe vanilla dialogue: {ex.Message}", LogLevel.Warn);
    }
}
