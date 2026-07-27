using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Valewake;

public sealed class NpcThinkingMenu : DialogueBox
{
    public NpcThinkingMenu(NPC npc)
        : base(new Dialogue(npc, null, $"*{npc.displayName} is thinking...*"))
    {
    }

    public override void receiveKeyPress(Keys key)
    {
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
    }

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
    }
}
