using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Valewake;

public sealed class NpcChatInputMenu : IClickableMenu
{
    private const int PreferredWidth = 1100;
    private const int MenuHeight = 360;
    private const int Margin = 32;
    private const int ButtonSize = 64;
    private const int TitleTopPadding = 88;
    private const int TextBoxTopPadding = 160;
    private readonly NPC npc;
    private readonly Action<string> onSubmit;
    private readonly Action onCancel;
    private readonly TextBox textBox;
    private readonly ClickableTextureComponent sendButton;
    private readonly ClickableTextureComponent endButton;
    private bool completed;

    public NpcChatInputMenu(NPC npc, Action<string> onSubmit, Action onCancel)
    {
        this.npc = npc;
        this.onSubmit = onSubmit;
        this.onCancel = onCancel;

        width = Math.Min(PreferredWidth, Game1.uiViewport.Width - 64);
        height = MenuHeight;
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(48, Game1.uiViewport.Height - height - 72);

        Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        textBox = new TextBox(textBoxTexture, null, Game1.dialogueFont, Game1.textColor)
        {
            X = xPositionOnScreen + Margin,
            Y = yPositionOnScreen + TextBoxTopPadding,
            Width = width - Margin * 3 - ButtonSize,
            Height = 64,
            limitWidth = true
        };
        textBox.OnEnterPressed += _ => Submit();
        textBox.SelectMe();

        sendButton = new ClickableTextureComponent(
            new Rectangle(xPositionOnScreen + width - Margin - ButtonSize, textBox.Y, ButtonSize, ButtonSize),
            Game1.mouseCursors,
            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46),
            1f
        )
        {
            hoverText = "Send"
        };

        endButton = new ClickableTextureComponent(
            new Rectangle(xPositionOnScreen + width - Margin - ButtonSize, yPositionOnScreen + height - Margin - ButtonSize, ButtonSize, ButtonSize),
            Game1.mouseCursors,
            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 47),
            1f
        )
        {
            hoverText = "End conversation"
        };
    }

    public override void update(GameTime time)
    {
        base.update(time);
        textBox.Update();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (sendButton.containsPoint(x, y))
        {
            Submit();
            return;
        }
        if (endButton.containsPoint(x, y))
        {
            Cancel();
            return;
        }
        if (new Rectangle(textBox.X, textBox.Y, textBox.Width, textBox.Height).Contains(x, y))
            textBox.SelectMe();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
            Cancel();
    }

    public override void performHoverAction(int x, int y)
    {
        sendButton.tryHover(x, y, 0.15f);
        endButton.tryHover(x, y, 0.15f);
    }

    protected override void cleanupBeforeExit()
    {
        textBox.Selected = false;
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, textBox))
            Game1.keyboardDispatcher.Subscriber = null;
        base.cleanupBeforeExit();
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.35f);
        Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

        string title = $"Talk to {npc.displayName}";
        b.DrawString(
            Game1.dialogueFont,
            title,
            new Vector2(xPositionOnScreen + Margin, yPositionOnScreen + TitleTopPadding),
            Game1.textColor
        );
        textBox.Draw(b);
        sendButton.draw(b);
        endButton.draw(b);

        string hint = "Enter: send    Esc: end conversation";
        b.DrawString(
            Game1.smallFont,
            hint,
            new Vector2(xPositionOnScreen + Margin, yPositionOnScreen + height - Margin - 24),
            Game1.unselectedOptionColor
        );

        string hover = sendButton.containsPoint(Game1.getMouseX(), Game1.getMouseY())
            ? sendButton.hoverText
            : endButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? endButton.hoverText : "";
        if (!string.IsNullOrEmpty(hover))
            drawHoverText(b, hover, Game1.smallFont);
        drawMouse(b);
    }

    private void Submit()
    {
        string text = textBox.Text.Trim();
        if (completed || string.IsNullOrWhiteSpace(text))
            return;
        completed = true;
        Game1.playSound("smallSelect");
        exitThisMenuNoSound();
        onSubmit(text);
    }

    private void Cancel()
    {
        if (completed)
            return;
        completed = true;
        Game1.playSound("cancel");
        exitThisMenuNoSound();
        onCancel();
    }
}
