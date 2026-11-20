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
    private readonly SdlImeBridge imeBridge;
    private readonly ClickableTextureComponent sendButton;
    private readonly ClickableTextureComponent endButton;
    private bool completed;
    private readonly Action<string>? onHistory;
    private readonly Rectangle historyButton;
    private readonly Action? onEndMeeting;
    private readonly Rectangle endMeetingButton;
    private readonly Action? onJoinMeeting;
    private readonly Rectangle joinMeetingButton;

    public NpcChatInputMenu(NPC npc, Action<string> onSubmit, Action onCancel, bool enableNativeImeCandidateWindow, Action<string>? onHistory = null, string draft = "", Action? onEndMeeting = null, int maxInputCharacters = 600, Action? onJoinMeeting = null)
    {
        this.npc = npc;
        this.onSubmit = onSubmit;
        this.onCancel = onCancel;
        this.onHistory = onHistory;
        this.onEndMeeting = onEndMeeting;
        this.onJoinMeeting = onJoinMeeting;

        width = Math.Min(PreferredWidth, Game1.uiViewport.Width - 64);
        height = MenuHeight;
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(48, Game1.uiViewport.Height - height - 72);
        historyButton = new Rectangle(xPositionOnScreen + width - Margin - 240, yPositionOnScreen + TitleTopPadding - 4, 208, 48);
        int meetingButtonWidth = Math.Min(208, Math.Max(80, (width - Margin * 3 - ButtonSize - 16) / 2));
        endMeetingButton = new Rectangle(xPositionOnScreen + Margin, yPositionOnScreen + height - Margin - ButtonSize, meetingButtonWidth, 48);
        joinMeetingButton = new Rectangle(endMeetingButton.Right + 16, endMeetingButton.Y, meetingButtonWidth, 48);

        Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        textBox = new TextBox(textBoxTexture, null, Game1.dialogueFont, Game1.textColor)
        {
            X = xPositionOnScreen + Margin,
            Y = yPositionOnScreen + TextBoxTopPadding,
            Width = width - Margin * 3 - ButtonSize,
            Height = 64,
            limitWidth = false,
            textLimit = Math.Clamp(maxInputCharacters, 100, 2000)
        };
        // Enter is submitted through ModEntry's SMAPI input handler so the key can
        // be suppressed before it reaches the game's fullscreen shortcut logic.
        textBox.Text = draft;
        textBox.SelectMe();
        imeBridge = new SdlImeBridge(enableNativeImeCandidateWindow);
        UpdateImeCandidatePosition();

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
        if (textBox.Text.Length > textBox.textLimit)
        {
            int limit = textBox.textLimit;
            if (limit > 0 && char.IsHighSurrogate(textBox.Text[limit - 1])) limit--;
            textBox.Text = textBox.Text[..limit];
        }
        textBox.Update();
        UpdateImeCandidatePosition();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (onJoinMeeting is not null && joinMeetingButton.Contains(x, y) && !completed)
        {
            completed = true;
            exitThisMenuNoSound();
            onJoinMeeting();
            return;
        }
        if (onEndMeeting is not null && endMeetingButton.Contains(x, y) && !completed)
        {
            completed = true;
            exitThisMenuNoSound();
            onEndMeeting();
            return;
        }
        if (onHistory is not null && historyButton.Contains(x, y) && !completed)
        {
            completed = true;
            string draft = textBox.Text;
            exitThisMenuNoSound();
            onHistory(draft);
            return;
        }
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
        {
            textBox.SelectMe();
            UpdateImeCandidatePosition();
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
            Cancel();
    }

    public void SubmitFromKeyboard() => Submit();

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
        imeBridge.Dispose();
        base.cleanupBeforeExit();
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.35f);
        Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

        string title = $"Talk to {npc.displayName}";
        float titleScale = Math.Min(1f, Math.Max(1, historyButton.X - xPositionOnScreen - Margin - 24) / Game1.dialogueFont.MeasureString(title).X);
        b.DrawString(
            Game1.dialogueFont,
            title,
            new Vector2(xPositionOnScreen + Margin, yPositionOnScreen + TitleTopPadding),
            Game1.textColor, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f
        );
        textBox.Draw(b);
        b.DrawString(Game1.smallFont, $"{textBox.Text.Length}/{textBox.textLimit}",
            new Vector2(textBox.X, textBox.Y + 68), Game1.unselectedOptionColor);
        if (onEndMeeting is not null)
        {
            drawTextureBox(b, endMeetingButton.X, endMeetingButton.Y, endMeetingButton.Width, endMeetingButton.Height, Color.White);
            string label = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh ? "结束约会" : "End meeting";
            DrawMeetingLabel(b, endMeetingButton, label);
        }
        if (onJoinMeeting is not null)
        {
            drawTextureBox(b, joinMeetingButton.X, joinMeetingButton.Y, joinMeetingButton.Width, joinMeetingButton.Height, Color.White);
            string label = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh ? "一起过去" : "Go together";
            DrawMeetingLabel(b, joinMeetingButton, label);
        }
        if (onHistory is not null)
        {
            drawTextureBox(b, historyButton.X, historyButton.Y, historyButton.Width, historyButton.Height, Color.White);
            string label = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh ? "今日对话" : "Today's dialogue";
            Vector2 size = Game1.smallFont.MeasureString(label);
            b.DrawString(Game1.smallFont, label, new Vector2(historyButton.Center.X - size.X / 2, historyButton.Center.Y - size.Y / 2), Game1.textColor);
        }
        sendButton.draw(b);
        endButton.draw(b);

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

    private static void DrawMeetingLabel(SpriteBatch b, Rectangle button, string label)
    {
        Vector2 size = Game1.smallFont.MeasureString(label);
        float scale = Math.Min(1f, (button.Width - 16) / size.X);
        b.DrawString(Game1.smallFont, label, new Vector2(button.Center.X - size.X * scale / 2, button.Center.Y - size.Y * scale / 2),
            Game1.textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
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

    private void UpdateImeCandidatePosition()
    {
        imeBridge.Update(new Rectangle(textBox.X, textBox.Y, textBox.Width, textBox.Height));
    }
}
