using System.Reflection;
using StardewValley;
using StardewValley.Menus;
using Valewake;

int tests = 0;
void Check(bool value, string label) { tests++; if (!value) throw new Exception(label); }
List<VanillaDialogueJournal.Entry> audit = new();
VanillaDialogueObserver.Install("test", new Monitor(), audit.Add);
NPC leah = new(), robin = new() { Name = "Robin" }, sam = new() { Name = "Sam", IsInvisible = true };
DialogueBox box = new() { characterDialogue = new() { speaker = leah }, Text = "这是我刚做好的木雕。" };
Game1.activeClickableMenu = box;
VanillaDialogueObserver.Observe(box);
for (int i = 0; i < 30; i++) VanillaDialogueObserver.Observe(box);
Check(VanillaDialogueJournal.ForNpc(Game1.player, "Leah").Length == 1, "same visible page only recorded once");
box.Text = "你觉得它怎么样？";
VanillaDialogueObserver.Observe(box);
Check(VanillaDialogueJournal.ForNpc(Game1.player, "Leah").Length == 2, "second page captured independently");
Check(audit.All(e => !e.text.Contains("NEVER")), "future branch never read");
DialogueBox generated = new() { characterDialogue = new() { speaker = leah }, Text = "AI output" };
VanillaDialogueObserver.MarkGenerated(generated);
VanillaDialogueObserver.Observe(generated);
Check(audit.Count == 2, "AI replies excluded from vanilla evidence");

Event story = new() { actors = new() { leah, robin, sam } };
Game1.CurrentEvent = story;
DialogueBox scene = new() { characterDialogue = new() { speaker = leah }, Text = "谢谢你们来看我的作品。" };
Game1.activeClickableMenu = scene;
VanillaDialogueObserver.Observe(scene);
Check(VanillaDialogueJournal.ForNpc(Game1.player, "Robin").Length == 1, "present participant knows witnessed line");
Check(VanillaDialogueJournal.ForNpc(Game1.player, "Sam").Length == 0, "hidden participant excluded");
Check(VanillaDialogueJournal.ForNpc(Game1.player, "Shane").Length == 0, "unrelated NPC excluded");
scene.responses = new[] { new Response { responseKey = "a", responseText = "我喜欢它" }, new Response { responseKey = "b", responseText = "还可以改进" } };
MethodInfo choose = typeof(VanillaDialogueObserver).GetMethod("BeforeStoryChoice", BindingFlags.NonPublic | BindingFlags.Static)!;
choose.Invoke(null, new object[] { story, 1 });
var selected = VanillaDialogueJournal.ForNpc(Game1.player, "Leah")[^1];
Check(selected.kind == "vanilla_choice" && selected.speaker == "Tester" && selected.text == "还可以改进", "only selected response recorded as player speech");
Check(audit.All(e => e.text != "我喜欢它"), "unselected option not a memory");
int before = audit.Count;
choose.Invoke(null, new object[] { story, 20 });
Check(before == audit.Count, "invalid selection ignored");
Check(selected.story_event_id == "leah_story", "story identity retained");
Farmer saved = new() { modData = new(Game1.player.modData) };
VanillaDialogueJournal.Append(Game1.player, new[] { "Leah" }, "Leah", "未保存的后续台词", "story");
Game1.player = saved;
Check(!VanillaDialogueJournal.ForNpc(Game1.player, "Leah").Any(e => e.text.Contains("未保存")), "unsaved story evidence rolls back with save");
Check(VanillaDialogueJournal.ForNpc(new Farmer(), "Leah").Length == 0, "player/save isolation");
for (int i = 0; i < 530; i++) VanillaDialogueJournal.Append(Game1.player, new[] { "Leah" }, "Leah", "line " + i, "story");
Check(VanillaDialogueJournal.ForNpc(Game1.player, "Leah").Length == 16, "bounded NPC context");
Console.WriteLine($"Vanilla dialogue: {tests}/{tests} passed (real observer/store, game and Harmony substitutes; not live interception).");

sealed class Monitor : StardewModdingAPI.IMonitor
{
    public void Log(string text, StardewModdingAPI.LogLevel level) => throw new Exception(text);
}
