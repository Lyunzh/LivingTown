using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace LivingTown
{
    public class LLMDialogueBox : IClickableMenu
    {
        private string _fullText = "";
        private string _displayText = "";
        private readonly Queue<string> _tokenQueue = new Queue<string>();
        private int _timer = 0;
        private bool _isStreaming = true;
        private Action _onClose;

        public LLMDialogueBox(Action onClose) : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height)
        {
            _onClose = onClose;
        }

        public void AppendText(string text)
        {
            foreach (char c in text)
            {
                _tokenQueue.Enqueue(c.ToString());
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (_isStreaming)
            {
                // Skip streaming and show all current text
                while (_tokenQueue.Count > 0)
                {
                    _displayText += _tokenQueue.Dequeue();
                }
                _isStreaming = false; // Or keeping it true if we expect more from API? 
                // For now, let's assume clicking just speeds up current queue.
            }
            else
            {
                // specific close logic or next page
                _onClose?.Invoke();
                exitThisMenu();
            }
        }

        public override void update(GameTime time)
        {
            base.update(time);
            _timer += time.ElapsedGameTime.Milliseconds;
            if (_timer > 20) // Speed of text appearance
            {
                _timer = 0;
                if (_tokenQueue.Count > 0)
                {
                    _displayText += _tokenQueue.Dequeue();
                    _isStreaming = true;
                }
                else
                {
                    _isStreaming = false;
                }
            }
        }

        public override void draw(SpriteBatch b)
        {
            // Draw standard dialogue box background
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), 
                0, Game1.uiViewport.Height - 300, Game1.uiViewport.Width, 300, Color.White, 4f, false);

            // Draw Text
             // Simplified drawing, normally would use SpriteText.drawString or similar
             // We need to handle word wrapping manually or use Game1.drawDialogueBox parsing
            
             // For prototype, simple draw
             b.DrawString(Game1.dialogueFont, _displayText, new Vector2(50, Game1.uiViewport.Height - 250), Color.Black);

             // Draw cursor if waiting for input
             if (!_isStreaming)
             {
                 base.drawMouse(b);
             }
        }
    }
}
