using LivingTown.Game;
using LivingTown.GOAP;
using LivingTown.LLM.Core;
using LivingTown.State;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace LivingTown;

public class ModEntry : Mod
{
    private ChatCoordinator _chat = null!;
    private GameStateTracker _stateTracker = null!;
    private HeuristicWatchdog _watchdog = null!;
    private MemoryManager _memoryManager = null!;
    private GiftEventBridge _giftBridge = null!;
    private Blackboard _blackboard = null!;
    private GOAPPlanner _planner = null!;
    private GoapActionExecutor _actionExecutor = null!;

    public override void Entry(IModHelper helper)
    {
        var dataDir = Path.Combine(helper.DirectoryPath, "data");
        var statePath = Path.Combine(dataDir, "game-state.json");
        var memoryPath = Path.Combine(dataDir, "memory-state.json");

        _stateTracker = new GameStateTracker(Monitor, statePath);
        _memoryManager = new MemoryManager(
            Monitor,
            memoryPath,
            () => new MemoryContext(Game1.Date.TotalDays, Game1.currentSeason, Game1.dayOfMonth));
        _watchdog = new HeuristicWatchdog(Monitor);
        _giftBridge = new GiftEventBridge(Monitor, _stateTracker, _watchdog, _memoryManager);
        var lexicalCache = new LexicalCache(Monitor);

        _blackboard = new Blackboard(Monitor);
        _planner = new GOAPPlanner(Monitor);
        _actionExecutor = new GoapActionExecutor(
            _blackboard,
            Monitor,
            (npcName, emoteId) => Game1.getCharacterFromName(npcName)?.doEmote(emoteId),
            (npcName, thought) => Game1.getCharacterFromName(npcName)?.showTextAboveHead(thought, duration: 3000));

        var soulLoader = new SoulLoader(Monitor);
        soulLoader.LoadAll(Path.Combine(helper.DirectoryPath, "assets", "souls"));

        var agentFactory = new AgentFactory(Monitor, helper.DirectoryPath);
        RegisterGameTools(agentFactory, Monitor, _blackboard, _memoryManager);

        _chat = new ChatCoordinator(
            Monitor, agentFactory, soulLoader,
            _stateTracker, _watchdog, lexicalCache, _memoryManager);

        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;

        Monitor.Log("[ModEntry] LivingTown v2 (Hybrid Architecture) loaded.", LogLevel.Info);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        _giftBridge.OnButtonPressed(e);

        if (!Context.IsWorldReady || !Context.IsPlayerFree || e.Button != SButton.C)
            return;

        var npc = Game1.currentLocation?.isCharacterAtTile(e.Cursor.GrabTile);
        if (npc == null)
            return;

        Game1.activeClickableMenu = new ChatInputMenu(npc.Name, _chat.OnPlayerChat);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        _giftBridge.OnInventoryChanged(e);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        _giftBridge.OnUpdateTicked(e);
        _chat.Tick();

        if (e.IsMultipleOf(60))
        {
            _blackboard.SyncFromGame(
                Game1.timeOfDay,
                Game1.currentSeason,
                Game1.isRaining ? "Rain" : "Sunny",
                Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth));
        }

        if (e.IsMultipleOf(120) && _blackboard.HasPendingGoals)
        {
            var goal = _blackboard.DequeueGoal();
            if (goal != null)
            {
                SeedNpcPlanningState(goal.NpcName);
                var plan = _planner.PlanFromGoal(_blackboard, goal);
                if (plan.Count > 0)
                {
                    Monitor.Log($"[GOAP] Plan for {goal.NpcName}: {string.Join(" → ", plan.Select(a => a.Name))}", LogLevel.Info);
                    _actionExecutor.ExecutePlan(goal.NpcName, plan);
                }
            }
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        _stateTracker.ResetDaily(GetDayKey());
        _watchdog.ResetAllEntropy();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        _stateTracker.RecordShipping(ReadShippingBin(), Game1.Date.TotalDays);
        _stateTracker.EndDay();
        _memoryManager.RunNightlyMaintenance(Game1.Date.TotalDays);
        Monitor.Log("[ModEntry] Day ending — state and memory persisted.", LogLevel.Debug);
    }

    private void SeedNpcPlanningState(string npcName)
    {
        var npc = Game1.getCharacterFromName(npcName);
        if (npc == null)
            return;

        _blackboard.SetNpcFact(npcName, "CurrentLocation", npc.currentLocation?.Name ?? "Town");
        if (_blackboard.GetNpcFact(npcName, "Mood") == null)
            _blackboard.SetNpcFact(npcName, "Mood", "Neutral");
    }

    private IEnumerable<ShippingRecord> ReadShippingBin()
    {
        var farm = Game1.getFarm();
        var shippingBin = farm.getShippingBin(Game1.player);
        if (shippingBin == null)
            return Array.Empty<ShippingRecord>();

        return shippingBin
            .Where(item => item != null)
            .GroupBy(item => item.DisplayName)
            .Select(group => new ShippingRecord
            {
                ItemName = group.Key,
                Quantity = group.Sum(item => item.Stack)
            })
            .ToArray();
    }

    private static string GetDayKey() =>
        $"Y{Game1.year}_Day{Game1.Date.TotalDays}";

    private static void RegisterGameTools(
        AgentFactory factory, IMonitor monitor,
        Blackboard blackboard, MemoryManager memoryManager)
    {
        factory.ToolRegistry.Register(GameTools.SetGoal(monitor, factory.ToolRegistry, blackboard));

        factory.ToolRegistry.Register(GameTools.PlayEmote(monitor, factory.ToolRegistry, (npc, emoteId) =>
        {
            var npcObj = Game1.getCharacterFromName(npc);
            npcObj?.doEmote(emoteId);
        }));

        factory.ToolRegistry.Register(GameTools.RememberFact(monitor, factory.ToolRegistry, (npc, fact, importance) =>
        {
            memoryManager.RecordEvent(npc, fact, importance);
        }));
    }
}

