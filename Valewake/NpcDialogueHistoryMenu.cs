using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Valewake;

internal sealed class NpcDialogueHistoryMenu : IClickableMenu
{
    private readonly NPC npc;
    private readonly Action onReturn;
    private readonly List<string> lines = new();
    private readonly bool chinese = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh;
    private int page;
    private int rows;
    private int lineHeight;
    private Rectangle previous, next, back;
    private bool closed;

    public NpcDialogueHistoryMenu(NPC npc, Action onReturn)
    {
        this.npc = npc;
        this.onReturn = onReturn;
        Layout();
        page = Math.Max(0, (lines.Count - 1) / rows);
    }

    private void Layout()
    {
        width = Math.Min(1000, Game1.uiViewport.Width - 48);
        height = Math.Min(720, Game1.uiViewport.Height - 48);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        lineHeight = Game1.smallFont.LineSpacing + 4;
        rows = Math.Max(1, (height - 160) / lineHeight);
        lines.Clear();
        foreach (DailyDialogueJournal.Entry entry in DailyDialogueJournal.ForNpc(npc.Name))
        {
            AddLines($"{entry.Time / 100:00}:{entry.Time % 100:00}  {Game1.player.Name}");
            AddLines(entry.PlayerText);
            AddLines(npc.displayName);
            AddLines(entry.Reply);
            lines.Add("");
        }
        if (lines.Count == 0)
            AddLines(chinese ? "今天还没有对话记录。" : "No conversations today.");
        page = Math.Clamp(page, 0, Math.Max(0, (lines.Count - 1) / rows));
        previous = new Rectangle(xPositionOnScreen + 32, yPositionOnScreen + height - 64, 64, 40);
        next = new Rectangle(xPositionOnScreen + 112, previous.Y, 64, 40);
        back = new Rectangle(xPositionOnScreen + width - 176, previous.Y, 144, 40);
    }

    private void AddLines(string text)
    {
        lines.AddRange(Game1.parseText(text.Replace("\r", ""), Game1.smallFont, width - 80).Split('\n'));
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();
    public override void receiveScrollWheelAction(int direction) => Turn(direction > 0 ? -1 : 1);
    private void Turn(int delta) => page = Math.Clamp(page + delta, 0, Math.Max(0, (lines.Count - 1) / rows));
    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape) Return();
        else if (key == Keys.Left || key == Keys.PageUp) Turn(-1);
        else if (key == Keys.Right || key == Keys.PageDown) Turn(1);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (back.Contains(x, y)) Return();
        else if (previous.Contains(x, y)) Turn(-1);
        else if (next.Contains(x, y)) Turn(1);
    }

    private void Return()
    {
        if (closed) return;
        closed = true;
        exitThisMenuNoSound();
        onReturn();
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * .5f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        string title = chinese ? $"{npc.displayName} · 今日对话" : $"{npc.displayName} · Today's dialogue";
        b.DrawString(Game1.smallFont, title, new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 28), Game1.textColor);
        for (int i = 0; i < rows && page * rows + i < lines.Count; i++)
            b.DrawString(Game1.smallFont, lines[page * rows + i], new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 80 + i * lineHeight), Game1.textColor);
        DrawButton(b, previous, "<");
        DrawButton(b, next, ">");
        DrawButton(b, back, chinese ? "返回" : "Back");
        b.DrawString(Game1.smallFont, $"{page + 1} / {Math.Max(1, (lines.Count + rows - 1) / rows)}",
            new Vector2(xPositionOnScreen + 200, previous.Y + 6), Game1.textColor);
        drawMouse(b);
    }

    private static void DrawButton(SpriteBatch b, Rectangle box, string text)
    {
        drawTextureBox(b, box.X, box.Y, box.Width, box.Height, Color.White);
        Vector2 size = Game1.smallFont.MeasureString(text);
        b.DrawString(Game1.smallFont, text, new Vector2(box.Center.X - size.X / 2, box.Center.Y - size.Y / 2), Game1.textColor);
    }
}
