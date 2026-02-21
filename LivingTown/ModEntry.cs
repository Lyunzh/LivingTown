using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using LivingTown.Game;
using LivingTown.GOAP;
using LivingTown.LLM.Core;
using LivingTown.State;

namespace LivingTown;

/// <summary>
/// Mod entry point — thin wiring layer only.
/// All business logic lives in ChatCoordinator, GOAP, and the State/Agent modules.
/// </summary>
public class ModEntry : Mod
{
    private ChatCoordinator _chat = null!;
    private GameStateTracker _stateTracker = null!;
    private HeuristicWatchdog _watchdog = null!;
    private MemoryManager _memoryManager = null!;
    private Blackboard _blackboard = null!;
    private GOAPPlanner _planner = null!;

    public override void Entry(IModHelper helper)
    {
        // ── Initialize services ──
        _stateTracker = new GameStateTracker(Monitor);
        _memoryManager = new MemoryManager(Monitor);
        _watchdog = new HeuristicWatchdog(Monitor);
        var lexicalCache = new LexicalCache(Monitor);

        // GOAP engine
        _blackboard = new Blackboard(Monitor);
        _planner = new GOAPPlanner(Monitor);

        var soulLoader = new SoulLoader(Monitor);
        soulLoader.LoadAll(System.IO.Path.Combine(helper.DirectoryPath, "assets", "souls"));

        var agentFactory = new AgentFactory(Monitor, helper.DirectoryPath);

        // Register game-specific tools (wired to real Blackboard + MemoryManager)
        RegisterGameTools(agentFactory, Monitor, _blackboard, _memoryManager);

        // ── Wire up ChatCoordinator ──
        _chat = new ChatCoordinator(
            Monitor, agentFactory, soulLoader,
            _stateTracker, _watchdog, lexicalCache, _memoryManager);

        // ── Subscribe to SMAPI events ──
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
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

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady) return;

        // Drain chat display queue
        _chat.Tick();

        // Sync world state to Blackboard (every 60 ticks ≈ 1 second)
        if (e.IsMultipleOf(60))
        {
            _blackboard.SyncFromGame(
                Game1.timeOfDay,
                Game1.currentSeason,
                Game1.isRaining ? "Rain" : "Sunny",
                Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth));
        }

        // Process pending GOAP goals (every 120 ticks ≈ 2 seconds)
        if (e.IsMultipleOf(120) && _blackboard.HasPendingGoals)
        {
            var goal = _blackboard.DequeueGoal();
            if (goal != null)
            {
                var plan = _planner.PlanFromGoal(_blackboard, goal);
                if (plan.Count > 0)
                {
                    Monitor.Log($"[GOAP] Plan for {goal.NpcName}: {string.Join(" → ", plan.Select(a => a.Name))}", LogLevel.Info);
                    // TODO: Execute plan actions (NPC pathfinding integration)
                }
            }
        }
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

    /// <summary>Register game-specific tools wired to real services.</summary>
    private static void RegisterGameTools(
        AgentFactory factory, IMonitor monitor,
        Blackboard blackboard, MemoryManager memoryManager)
    {
        // set_goal → writes to REAL Blackboard (processed by Planner on tick)
        factory.ToolRegistry.Register(GameTools.SetGoal(monitor, blackboard));

        factory.ToolRegistry.Register(GameTools.PlayEmote(monitor, (npc, emoteId) =>
        {
            var npcObj = Game1.getCharacterFromName(npc);
            npcObj?.doEmote(emoteId);
        }));

        // remember → writes to REAL MemoryManager
        factory.ToolRegistry.Register(GameTools.RememberFact(monitor, (npc, fact, importance) =>
        {
            memoryManager.RecordEvent(npc, fact, importance);
        }));
    }
}
