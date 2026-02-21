using StardewModdingAPI;

namespace LivingTown.GOAP;

/// <summary>
/// The GOAP Blackboard: a flat key-value state dictionary shared between
/// the Agent (writer via set_goal tool) and the Planner (reader).
///
/// Two layers:
///   - World state: global facts (Saloon_IsOpen, CurrentTime, Weather)
///   - NPC state: per-NPC facts (IsHungry, CurrentLocation, Mood)
///
/// The Planner reads the current state and a goal state, then finds
/// an action sequence to transform current → goal.
/// </summary>
public class Blackboard
{
    private readonly IMonitor _monitor;

    /// <summary>Global world state facts.</summary>
    private readonly Dictionary<string, object> _worldState = new();

    /// <summary>Per-NPC state facts. Key = "NpcName.FactName".</summary>
    private readonly Dictionary<string, object> _npcStates = new();

    /// <summary>Pending goals queue. Planner consumes these each tick.</summary>
    private readonly Queue<Goal> _pendingGoals = new();

    public Blackboard(IMonitor monitor)
    {
        _monitor = monitor;
    }

    // ── World State ──

    public void SetWorldFact(string key, object value)
    {
        _worldState[key] = value;
        _monitor.Log($"[Blackboard] World: {key} = {value}", LogLevel.Trace);
    }

    public object? GetWorldFact(string key) =>
        _worldState.TryGetValue(key, out var v) ? v : null;

    public bool GetWorldBool(string key) =>
        _worldState.TryGetValue(key, out var v) && v is true;

    // ── NPC State ──

    public void SetNpcFact(string npcName, string key, object value)
    {
        _npcStates[$"{npcName}.{key}"] = value;
        _monitor.Log($"[Blackboard] {npcName}.{key} = {value}", LogLevel.Trace);
    }

    public object? GetNpcFact(string npcName, string key) =>
        _npcStates.TryGetValue($"{npcName}.{key}", out var v) ? v : null;

    public bool GetNpcBool(string npcName, string key) =>
        _npcStates.TryGetValue($"{npcName}.{key}", out var v) && v is true;

    public string? GetNpcString(string npcName, string key) =>
        _npcStates.TryGetValue($"{npcName}.{key}", out var v) ? v.ToString() : null;

    /// <summary>Get a snapshot of all NPC facts for a given NPC.</summary>
    public Dictionary<string, object> GetNpcSnapshot(string npcName)
    {
        var snapshot = new Dictionary<string, object>();
        var prefix = $"{npcName}.";
        foreach (var kvp in _npcStates)
            if (kvp.Key.StartsWith(prefix))
                snapshot[kvp.Key[prefix.Length..]] = kvp.Value;
        return snapshot;
    }

    // ── Goals ──

    /// <summary>Enqueue a goal from the Agent's set_goal tool.</summary>
    public void EnqueueGoal(Goal goal)
    {
        _pendingGoals.Enqueue(goal);
        _monitor.Log($"[Blackboard] Goal enqueued: {goal.NpcName} → {goal.GoalKey}={goal.GoalValue} (priority={goal.Priority})", LogLevel.Info);
    }

    /// <summary>Dequeue the next pending goal. Returns null if queue is empty.</summary>
    public Goal? DequeueGoal() =>
        _pendingGoals.Count > 0 ? _pendingGoals.Dequeue() : null;

    /// <summary>Check if there are pending goals.</summary>
    public bool HasPendingGoals => _pendingGoals.Count > 0;

    // ── Sync from game ──

    /// <summary>
    /// Update world state from game data. Call once per tick or on relevant events.
    /// </summary>
    public void SyncFromGame(int timeOfDay, string season, string weather, string dayOfWeek)
    {
        _worldState["TimeOfDay"] = timeOfDay;
        _worldState["Season"] = season;
        _worldState["Weather"] = weather;
        _worldState["DayOfWeek"] = dayOfWeek;
        _worldState["Saloon_IsOpen"] = timeOfDay >= 1200 && timeOfDay < 2400;
        _worldState["Clinic_IsOpen"] = dayOfWeek != "Wednesday" && timeOfDay >= 900 && timeOfDay < 1500;
    }
}

/// <summary>A goal produced by the Agent's set_goal tool.</summary>
public class Goal
{
    public string NpcName { get; set; } = "";
    public string GoalKey { get; set; } = "";
    public object GoalValue { get; set; } = true;
    public GoalPriority Priority { get; set; } = GoalPriority.Medium;
    public string Reason { get; set; } = "";
}

public enum GoalPriority { Low, Medium, High }
