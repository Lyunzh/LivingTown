using StardewModdingAPI;

namespace LivingTown.GOAP;

/// <summary>
/// GOAP Planner: given a current state and a goal state, finds the cheapest
/// sequence of actions to reach the goal using backward A* search.
///
/// Algorithm:
///   1. Start from the goal state.
///   2. For each unsatisfied goal fact, find actions whose EFFECTS produce it.
///   3. Prepend the action, apply its preconditions as new sub-goals.
///   4. Repeat until all preconditions are met by the current world state.
///   5. Return the action list in execution order.
/// </summary>
public class GOAPPlanner
{
    private readonly IMonitor _monitor;
    private readonly List<GOAPAction> _availableActions;
    private const int MaxSearchDepth = 10;

    public GOAPPlanner(IMonitor monitor, List<GOAPAction>? actions = null)
    {
        _monitor = monitor;
        _availableActions = actions ?? ActionLibrary.GetDefaultActions();
    }

    /// <summary>
    /// Plan a sequence of actions to achieve the goal from the current state.
    /// Returns an empty list if no valid plan is found.
    /// </summary>
    public List<GOAPAction> Plan(Dictionary<string, object> currentState, Dictionary<string, object> goalState)
    {
        _monitor.Log($"[Planner] Planning: current={FormatState(currentState)} → goal={FormatState(goalState)}", LogLevel.Debug);

        var rootNode = new PlanNode
        {
            State = new Dictionary<string, object>(currentState),
            Actions = new List<GOAPAction>(),
            Cost = 0f
        };

        // BFS with cost tracking (simplified A* — no heuristic, just Dijkstra)
        var openList = new PriorityQueue<PlanNode, float>();
        openList.Enqueue(rootNode, 0f);

        var visited = new HashSet<string>();
        int iterations = 0;

        while (openList.Count > 0 && iterations < 200)
        {
            iterations++;
            var current = openList.Dequeue();

            // Check if goal is satisfied
            if (IsGoalSatisfied(current.State, goalState))
            {
                _monitor.Log($"[Planner] ✅ Plan found! {current.Actions.Count} actions, cost={current.Cost:F1}, iterations={iterations}", LogLevel.Info);
                foreach (var a in current.Actions)
                    _monitor.Log($"  → {a.Name} (cost={a.Cost})", LogLevel.Debug);
                return current.Actions;
            }

            // Skip if we've seen this state
            var stateKey = StateKey(current.State);
            if (!visited.Add(stateKey))
                continue;

            // Depth limit
            if (current.Actions.Count >= MaxSearchDepth)
                continue;

            // Try each available action
            foreach (var action in _availableActions)
            {
                if (!action.ArePreconditionsMet(current.State))
                    continue;

                var newState = action.ApplyEffects(current.State);
                var newActions = new List<GOAPAction>(current.Actions) { action };
                var newCost = current.Cost + action.Cost;

                openList.Enqueue(new PlanNode
                {
                    State = newState,
                    Actions = newActions,
                    Cost = newCost
                }, newCost);
            }
        }

        _monitor.Log($"[Planner] ❌ No plan found after {iterations} iterations.", LogLevel.Warn);
        return new List<GOAPAction>();
    }

    /// <summary>
    /// Convenience: plan from a Goal object (as produced by the Agent's set_goal tool).
    /// </summary>
    public List<GOAPAction> PlanFromGoal(Blackboard blackboard, Goal goal)
    {
        var currentState = blackboard.GetNpcSnapshot(goal.NpcName);

        // Inject world state into the NPC's planning context
        var worldSaloon = blackboard.GetWorldFact("Saloon_IsOpen");
        if (worldSaloon != null)
            currentState["Saloon_IsOpen"] = worldSaloon;
        var worldClinic = blackboard.GetWorldFact("Clinic_IsOpen");
        if (worldClinic != null)
            currentState["Clinic_IsOpen"] = worldClinic;

        var goalState = new Dictionary<string, object>
        {
            [goal.GoalKey] = goal.GoalValue
        };

        return Plan(currentState, goalState);
    }

    private static bool IsGoalSatisfied(Dictionary<string, object> state, Dictionary<string, object> goal)
    {
        foreach (var g in goal)
        {
            if (!state.TryGetValue(g.Key, out var val))
                return false;
            if (!val.Equals(g.Value))
                return false;
        }
        return true;
    }

    private static string StateKey(Dictionary<string, object> state)
    {
        var sorted = state.OrderBy(kvp => kvp.Key)
            .Select(kvp => $"{kvp.Key}={kvp.Value}");
        return string.Join("|", sorted);
    }

    private static string FormatState(Dictionary<string, object> state)
    {
        return "{" + string.Join(", ", state.Select(kvp => $"{kvp.Key}={kvp.Value}")) + "}";
    }

    private class PlanNode
    {
        public Dictionary<string, object> State { get; set; } = new();
        public List<GOAPAction> Actions { get; set; } = new();
        public float Cost { get; set; }
    }
}
