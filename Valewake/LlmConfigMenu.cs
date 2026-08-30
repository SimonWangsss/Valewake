using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace Valewake;

/// <summary>
/// In-game menu for picking an LLM provider, pasting an API key, and choosing a model.
/// The official URL is filled automatically from the selected provider; only the
/// "custom / local" provider asks the user to type a URL.
/// </summary>
public sealed class LlmConfigMenu : IClickableMenu
{
    private const int Margin = 28;
    private const int RowHeight = 32;
    private const int ButtonSize = 64;

    private readonly LlmConfigResponse config;
    private readonly Action<LlmConfigRequest> onSave;
    private readonly Action onCancel;
    private readonly TextBox apiKeyBox;
    private readonly TextBox customModelBox;
    private readonly ClickableTextureComponent saveButton;
    private readonly ClickableTextureComponent cancelButton;
    private readonly List<Rectangle> providerRows = new();
    private readonly List<Rectangle> modelRows = new();
    private int selectedProvider = -1;
    private int selectedModel = -1;
    private bool completed;
    private string status = "";

    public LlmConfigMenu(LlmConfigResponse config, Action<LlmConfigRequest> onSave, Action onCancel)
    {
        this.config = config;
        this.onSave = onSave;
        this.onCancel = onCancel;

        width = 760;
        height = 640;
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
        yPositionOnScreen = Math.Max(24, (Game1.uiViewport.Height - height) / 2);

        for (int i = 0; i < config.Providers.Count; i++)
        {
            if (config.Providers[i].Id == config.Current.Provider)
                selectedProvider = i;
        }
        if (selectedProvider >= 0)
        {
            List<string> models = config.Providers[selectedProvider].Models;
            for (int i = 0; i < models.Count; i++)
            {
                if (models[i] == config.Current.Model)
                    selectedModel = i;
            }
        }

        // Rows are laid out top-to-bottom; computed once.
        int providerTop = yPositionOnScreen + 92;
        for (int i = 0; i < config.Providers.Count; i++)
            providerRows.Add(new Rectangle(xPositionOnScreen + Margin, providerTop + i * RowHeight, width / 2 - Margin * 2, RowHeight));

        int modelTop = yPositionOnScreen + 92;
        for (int i = 0; i < Math.Max(3, ModelCount()); i++)
            modelRows.Add(new Rectangle(xPositionOnScreen + width / 2 + Margin, modelTop + i * RowHeight, width / 2 - Margin * 2, RowHeight));

        Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
        apiKeyBox = new TextBox(textBoxTexture, null, Game1.dialogueFont, Game1.textColor)
        {
            X = xPositionOnScreen + width / 2 + Margin,
            Y = yPositionOnScreen + height - 220,
            Width = width / 2 - Margin * 2 - 4,
            Height = 60,
            limitWidth = true
        };
        apiKeyBox.OnEnterPressed += _ => Save();

        customModelBox = new TextBox(textBoxTexture, null, Game1.dialogueFont, Game1.textColor)
        {
            X = xPositionOnScreen + width / 2 + Margin,
            Y = yPositionOnScreen + height - 140,
            Width = width / 2 - Margin * 2 - 4,
            Height = 60,
            limitWidth = true
        };
        customModelBox.Text = config.Current.Model;
        customModelBox.OnEnterPressed += _ => Save();

        saveButton = new ClickableTextureComponent(
            new Rectangle(xPositionOnScreen + width - Margin - ButtonSize, yPositionOnScreen + height - Margin - ButtonSize, ButtonSize, ButtonSize),
            Game1.mouseCursors,
            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46),
            1f
        ) { hoverText = "保存" };

        cancelButton = new ClickableTextureComponent(
            new Rectangle(xPositionOnScreen + width - Margin - ButtonSize * 2 - 8, yPositionOnScreen + height - Margin - ButtonSize, ButtonSize, ButtonSize),
            Game1.mouseCursors,
            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 47),
            1f
        ) { hoverText = "取消" };
    }

    private int ModelCount() => selectedProvider >= 0 ? config.Providers[selectedProvider].Models.Count + 1 : 1;

    public override void update(GameTime time)
    {
        base.update(time);
        apiKeyBox.Update();
        customModelBox.Update();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        for (int i = 0; i < providerRows.Count && i < config.Providers.Count; i++)
        {
            if (providerRows[i].Contains(x, y))
            {
                selectedProvider = i;
                selectedModel = -1;
                customModelBox.Text = "";
                Game1.playSound("smallSelect");
                return;
            }
        }

        if (selectedProvider >= 0)
        {
            List<string> models = config.Providers[selectedProvider].Models;
            for (int i = 0; i < modelRows.Count; i++)
            {
                if (!modelRows[i].Contains(x, y))
                    continue;
                if (i < models.Count)
                {
                    selectedModel = i;
                    customModelBox.Text = models[i];
                }
                else
                {
                    // "custom model" row
                    selectedModel = -1;
                    customModelBox.Text = "";
                    customModelBox.SelectMe();
                }
                Game1.playSound("smallSelect");
                return;
            }
        }

        if (saveButton.containsPoint(x, y)) { Save(); return; }
        if (cancelButton.containsPoint(x, y)) { Cancel(); return; }
        if (new Rectangle(apiKeyBox.X, apiKeyBox.Y, apiKeyBox.Width, apiKeyBox.Height).Contains(x, y)) apiKeyBox.SelectMe();
        if (new Rectangle(customModelBox.X, customModelBox.Y, customModelBox.Width, customModelBox.Height).Contains(x, y)) customModelBox.SelectMe();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
            Cancel();
    }

    public override void performHoverAction(int x, int y)
    {
        saveButton.tryHover(x, y, 0.15f);
        cancelButton.tryHover(x, y, 0.15f);
    }

    protected override void cleanupBeforeExit()
    {
        apiKeyBox.Selected = false;
        customModelBox.Selected = false;
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, apiKeyBox)) Game1.keyboardDispatcher.Subscriber = null;
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, customModelBox)) Game1.keyboardDispatcher.Subscriber = null;
        base.cleanupBeforeExit();
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);
        Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, false, true);

        b.DrawString(Game1.dialogueFont, "LLM 模型设置", new Vector2(xPositionOnScreen + Margin, yPositionOnScreen + 32), Game1.textColor);

        b.DrawString(Game1.smallFont, "服务商（自动带出官方地址）", new Vector2(xPositionOnScreen + Margin, yPositionOnScreen + 68), Game1.unselectedOptionColor);
        for (int i = 0; i < config.Providers.Count; i++)
        {
            Rectangle row = providerRows[i];
            bool selected = i == selectedProvider;
            b.DrawString(
                Game1.smallFont,
                config.Providers[i].Name,
                new Vector2(row.X, row.Y + 6),
                selected ? Color.Yellow : Game1.textColor
            );
        }

        b.DrawString(Game1.smallFont, "模型", new Vector2(xPositionOnScreen + width / 2 + Margin, yPositionOnScreen + 68), Game1.unselectedOptionColor);
        if (selectedProvider >= 0)
        {
            LlmProviderOption provider = config.Providers[selectedProvider];
            if (!string.IsNullOrEmpty(provider.BaseUrl))
                b.DrawString(Game1.smallFont, provider.BaseUrl, new Vector2(xPositionOnScreen + width / 2 + Margin, yPositionOnScreen + 68 + (modelRows.Count + 1) * RowHeight + 4), Color.DarkGray);

            List<string> models = provider.Models;
            for (int i = 0; i < modelRows.Count; i++)
            {
                string label = i < models.Count ? models[i] : "自定义模型…";
                bool selected = i == selectedModel;
                b.DrawString(
                    Game1.smallFont,
                    label,
                    new Vector2(modelRows[i].X, modelRows[i].Y + 6),
                    selected ? Color.Yellow : Game1.textColor
                );
            }
        }

        b.DrawString(Game1.smallFont, "API Key", new Vector2(apiKeyBox.X, apiKeyBox.Y - 24), Game1.unselectedOptionColor);
        apiKeyBox.Draw(b);
        b.DrawString(Game1.smallFont, "模型名（可手填）", new Vector2(customModelBox.X, customModelBox.Y - 24), Game1.unselectedOptionColor);
        customModelBox.Draw(b);

        saveButton.draw(b);
        cancelButton.draw(b);

        if (!string.IsNullOrEmpty(status))
            b.DrawString(Game1.smallFont, status, new Vector2(xPositionOnScreen + Margin, yPositionOnScreen + height - 32), Color.Orange);

        string hover = saveButton.containsPoint(Game1.getMouseX(), Game1.getMouseY())
            ? saveButton.hoverText
            : cancelButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? cancelButton.hoverText : "";
        if (!string.IsNullOrEmpty(hover))
            drawHoverText(b, hover, Game1.smallFont);
        drawMouse(b);
    }

    private void Save()
    {
        if (completed)
            return;
        if (selectedProvider < 0)
        {
            status = "请先选择一个服务商";
            return;
        }
        string apiKey = apiKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            status = "请填写 API Key";
            apiKeyBox.SelectMe();
            return;
        }
        string model = customModelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            status = "请选择或填写模型名";
            return;
        }

        LlmProviderOption provider = config.Providers[selectedProvider];
        completed = true;
        Game1.playSound("smallSelect");
        exitThisMenuNoSound();
        onSave(new LlmConfigRequest
        {
            Provider = provider.Id,
            ApiBase = provider.BaseUrl,
            ApiKey = apiKey,
            Model = model,
            Backend = provider.Format,
        });
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
