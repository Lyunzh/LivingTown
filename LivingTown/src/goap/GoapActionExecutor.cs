using StardewModdingAPI;

namespace LivingTown.GOAP;

public sealed class GoapActionExecutor
{
    private readonly IMonitor? _monitor;
    private readonly Blackboard _blackboard;
    private readonly Action<string, int>? _playEmote;
    private readonly Action<string, string>? _showThought;

    public GoapActionExecutor(
        Blackboard blackboard,
        IMonitor? monitor = null,
        Action<string, int>? playEmote = null,
        Action<string, string>? showThought = null)
    {
        _blackboard = blackboard;
        _monitor = monitor;
        _playEmote = playEmote;
        _showThought = showThought;
    }

    public void ExecutePlan(string npcName, IReadOnlyList<GOAPAction> plan)
    {
        foreach (var action in plan)
            ExecuteAction(npcName, action);
    }

    public void ExecuteAction(string npcName, GOAPAction action)
    {
        foreach (var effect in action.Effects)
            _blackboard.SetNpcFact(npcName, effect.Key, effect.Value);

        if (action.Name.StartsWith("WalkTo_", StringComparison.OrdinalIgnoreCase))
        {
            _playEmote?.Invoke(npcName, 16);
            _showThought?.Invoke(npcName, $"Heading to {action.Effects.GetValueOrDefault("CurrentLocation", "somewhere")}." );
        }
        else if (action.Name.Equals("Eat", StringComparison.OrdinalIgnoreCase))
        {
            _playEmote?.Invoke(npcName, 32);
            _showThought?.Invoke(npcName, "Finally. Food.");
        }
        else if (action.Name.Equals("SitAndBrood", StringComparison.OrdinalIgnoreCase))
        {
            _playEmote?.Invoke(npcName, 28);
            _showThought?.Invoke(npcName, "I need a minute.");
        }
        else if (action.Name.Equals("PlayPool", StringComparison.OrdinalIgnoreCase))
        {
            _playEmote?.Invoke(npcName, 20);
            _showThought?.Invoke(npcName, "This should help.");
        }

        _monitor?.Log($"[GOAP] Executed action for {npcName}: {action.Name}", LogLevel.Info);
    }
}
