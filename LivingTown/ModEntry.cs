using StardewModdingAPI;

namespace LivingTown;

/// <summary>
/// Mod entry point. Thin glue layer — wires agents and pipeline together.
/// All game logic lives in Game.Agent.
/// </summary>
public class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        // Create agents
        var gameAgent = new Game.Agent(Monitor, helper);
        var llmAgent = new LLM.Agent(Monitor, helper.DirectoryPath);
        var npcAgent = new Npc.Agent(Monitor);

        // Create pipeline
        var pipeline = new Pipeline.Pipeline(Monitor, gameAgent, llmAgent, npcAgent);

        // Wire pipeline back to game agent (for session creation)
        gameAgent.SetPipeline(pipeline);

        // Register SMAPI event handlers (all in Game.Agent)
        gameAgent.RegisterEvents();

        Monitor.Log("[ModEntry] LivingTown loaded.", LogLevel.Info);
    }
}
