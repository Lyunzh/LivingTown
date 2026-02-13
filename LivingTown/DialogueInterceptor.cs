using System;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace LivingTown
{
    public class DialogueInterceptor
    {
        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        private readonly LLMService _llmService;

        public DialogueInterceptor(IModHelper helper, IMonitor monitor, LLMService llmService)
        {
            _helper = helper;
            _monitor = monitor;
            _llmService = llmService;

            _helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsPlayerFree) return;

            if (e.Button.IsActionButton())
            {
                var cursorTile = e.Cursor.Tile;
                var clickedNpc = Game1.currentLocation.isCharacterAtTile(cursorTile);

                if (clickedNpc != null)
                {
                    // Suppress default interaction immediately to avoid vanilla dialogue box appearing first
                    _helper.Input.Suppress(e.Button);

                    // Create the choice menu
                    Game1.currentLocation.createQuestionDialogue(
                        $"Chat with {clickedNpc.Name}?",
                        new[]
                        {
                            new Response("Standard", "Standard Chat"),
                            new Response("Deep", "Deep Chat (LLM)")
                        },
                        (f, answer) =>
                        {
                            if (answer == "Deep")
                            {
                                StartLLMChat(clickedNpc);
                            }
                            else
                            {
                                // Manually trigger the vanilla checkAction since we suppressed the input
                                // We need to be careful not to loop. 
                                // Since we are in a delegate, we are no longer in the details of the button press.
                                clickedNpc.checkAction(Game1.player, Game1.currentLocation);
                            }
                        }
                    );
                }
            }
        }

        public async void StartLLMChat(NPC npc)
        {
            // Close any existing menus (like the question dialogue)
            Game1.exitActiveMenu();
            
            // Open our custom streaming dialogue box
            var menu = new LLMDialogueBox(() => { Game1.exitActiveMenu(); });
            Game1.activeClickableMenu = menu;
            
            menu.AppendText($"[System] Connecting to {npc.Name}...\n");

            try
            {
                // In a real scenario, we'd get the prompt from player input or context
                // For now, hardcoded trigger "Hello"
                await foreach (var token in _llmService.GetDialogueStreamAsync(npc.Name, "Hello! Tell me about your day."))
                {
                    menu.AppendText(token);
                }
            }
            catch (Exception ex)
            {
                menu.AppendText($"\n[Error] {ex.Message}");
            }
        }
    }
}
