using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace LivingTown
{
    public class ModEntry : Mod
    {
        private LLMService _llmService;
        private DialogueInterceptor _dialogueInterceptor;

        public override void Entry(IModHelper helper)
        {
            // Initialize services
            _llmService = new LLMService(Monitor);
            _dialogueInterceptor = new DialogueInterceptor(helper, Monitor, _llmService);

            // Hook events
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Perform any post-launch initialization if needed
             Monitor.Log("LivingTown Mod Loaded: Ready for NPC interactions.", LogLevel.Debug);
        }
    }
}
