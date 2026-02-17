using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace LivingTown.Game;

/// <summary>
/// A simple chat input menu that opens when the player right-clicks an NPC.
/// Uses Stardew Valley's built-in TextBox for text input.
/// On submit, fires a callback with the typed text and closes.
/// </summary>
public class ChatInputMenu : IClickableMenu
{
    private readonly string _npcName;
    private readonly Action<string, string> _onSubmit; // (npcName, message)
    private readonly TextBox _textBox;
    private readonly ClickableTextureComponent _submitButton;

    private const int MenuWidth = 600;
    private const int MenuHeight = 200;

    public ChatInputMenu(string npcName, Action<string, string> onSubmit)
        : base(
            (Game1.uiViewport.Width - MenuWidth) / 2,
            (Game1.uiViewport.Height - MenuHeight) / 2,
            MenuWidth,
            MenuHeight,
            showUpperRightCloseButton: true
        )
    {
        _npcName = npcName;
        _onSubmit = onSubmit;

        // Create TextBox for input
        _textBox = new TextBox(
            textBoxTexture: Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
            null,
            Game1.smallFont,
            Game1.textColor
        )
        {
            X = xPositionOnScreen + 32,
            Y = yPositionOnScreen + 96,
            Width = MenuWidth - 128,
            Text = ""
        };
        _textBox.Selected = true;

        // Submit button (use the OK button texture)
        _submitButton = new ClickableTextureComponent(
            new Rectangle(
                xPositionOnScreen + MenuWidth - 96,
                yPositionOnScreen + 80,
                64, 64
            ),
            Game1.mouseCursors,
            new Rectangle(128, 256, 64, 64), // OK button sprite
            1f
        );

        // Capture keyboard for the text box
        Game1.keyboardDispatcher.Subscriber = _textBox;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);

        // Check submit button
        if (_submitButton.containsPoint(x, y))
        {
            Submit();
            return;
        }

        // Click on text box to focus
        _textBox.Update();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Enter || key == Keys.Tab)
        {
            Submit();
            return;
        }

        if (key == Keys.Escape)
        {
            exitThisMenu();
            return;
        }
    }

    private void Submit()
    {
        var text = _textBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            _onSubmit?.Invoke(_npcName, text);
        }
        exitThisMenu();
    }

    public override void draw(SpriteBatch b)
    {
        // Draw semi-transparent backdrop
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

        // Draw menu background
        drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen,
            width, height,
            Color.White
        );

        // Draw title: "Talk to [NPC Name]"
        var title = $"Talk to {_npcName}";
        var titleSize = Game1.dialogueFont.MeasureString(title);
        b.DrawString(
            Game1.dialogueFont,
            title,
            new Vector2(xPositionOnScreen + (width - titleSize.X) / 2, yPositionOnScreen + 20),
            Game1.textColor
        );

        // Draw text input box
        _textBox.Draw(b);

        // Draw submit button
        _submitButton.draw(b);

        // Draw close button
        base.draw(b);

        // Draw cursor
        drawMouse(b);
    }
}
