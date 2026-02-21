using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using LivingTown.Game;
using LivingTown.LLM.Core;
using LivingTown.State;

namespace LivingTown;

/// <summary>
/// Mod entry point — thin wiring layer only.
/// All business logic lives in ChatCoordinator and the State/Agent modules.
/// </summary>
public class ModEntry : Mod
{
    private ChatCoordinator _chat = null!;
    private GameStateTracker _stateTracker = null!;
    private HeuristicWatchdog _watchdog = null!;
    private MemoryManager _memoryManager = null!;

    public override void Entry(IModHelper helper)
    {
        // ── Initialize services ──
        _stateTracker = new GameStateTracker(Monitor);
        _memoryManager = new MemoryManager(Monitor);
        _watchdog = new HeuristicWatchdog(Monitor);
        var lexicalCache = new LexicalCache(Monitor);

        var soulLoader = new SoulLoader(Monitor);
        soulLoader.LoadAll(System.IO.Path.Combine(helper.DirectoryPath, "assets", "souls"));

        var agentFactory = new AgentFactory(Monitor, helper.DirectoryPath);

        // Register game-specific tools on the agent factory
        RegisterGameTools(agentFactory, Monitor);

        // ── Wire up ChatCoordinator ──
        _chat = new ChatCoordinator(
            Monitor, agentFactory, soulLoader,
            _stateTracker, _watchdog, lexicalCache, _memoryManager);

        // ── Subscribe to SMAPI events ──
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.GameLoop.UpdateTicked += (_, _) => { if (Context.IsWorldReady) _chat.Tick(); };
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;

        Monitor.Log("[ModEntry] LivingTown v2 (Hybrid Architecture) loaded.", LogLevel.Info);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree || e.Button != SButton.C) return;

        var npc = Game1.currentLocation?.isCharacterAtTile(e.Cursor.GrabTile);
        if (npc == null) return;

        Game1.activeClickableMenu = new ChatInputMenu(npc.Name, _chat.OnPlayerChat);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        _stateTracker.ResetDaily();
        _watchdog.ResetAllEntropy();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        Monitor.Log("[ModEntry] Day ending — nightly batch pending.", LogLevel.Debug);
    }

    /// <summary>Register game-specific tools (GOAP bridge, emotes, memory, etc.)</summary>
    private static void RegisterGameTools(AgentFactory factory, IMonitor monitor)
    {
        factory.ToolRegistry.Register(GameTools.SetGoal(monitor));

        factory.ToolRegistry.Register(GameTools.PlayEmote(monitor, (npc, emoteId) =>
        {
            // Enqueue emote to be played on main thread via Tick
            var npcObj = Game1.getCharacterFromName(npc);
            npcObj?.doEmote(emoteId);
        }));

        factory.ToolRegistry.Register(GameTools.RememberFact(monitor, (npc, fact, importance) =>
        {
            monitor.Log($"[GameTools] Remember: {fact} (importance={importance})", LogLevel.Debug);
            // Memory recording will be handled by coordinator
        }));

        // WebSearch is NOT registered for NpcChat mode (too slow for dialogue).
        // It's available for PersonaBuilder and nightly batch modes.
    }
}
