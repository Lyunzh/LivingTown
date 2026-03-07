using StardewModdingAPI;

namespace LivingTown.GOAP;

public sealed class GoapDebugCommands
{
    private readonly IMonitor _monitor;
    private readonly Blackboard _blackboard;
    private readonly GOAPPlanner _planner;
    private readonly GoapActionExecutor _executor;
    private readonly Action<string> _seedNpcState;

    public GoapDebugCommands(
        IMonitor monitor,
        Blackboard blackboard,
        GOAPPlanner planner,
        GoapActionExecutor executor,
        Action<string> seedNpcState)
    {
        _monitor = monitor;
        _blackboard = blackboard;
        _planner = planner;
        _executor = executor;
        _seedNpcState = seedNpcState;
    }

    public void Register(IModHelper helper)
    {
        helper.ConsoleCommands.Add("lt_goap_help", "Show LivingTown GOAP debug commands.", OnHelp);
        helper.ConsoleCommands.Add("lt_goap_goal", "Execute a GOAP goal immediately. Usage: lt_goap_goal <npc> <GoalKey=Value> [priority] [reason]", OnGoal);
        helper.ConsoleCommands.Add("lt_goap_action", "Execute a GOAP action immediately. Usage: lt_goap_action <npc> <ActionName>", OnAction);
        helper.ConsoleCommands.Add("lt_goap_state", "Print current GOAP state for an NPC. Usage: lt_goap_state <npc>", OnState);
        helper.ConsoleCommands.Add("lt_goap_actions", "List built-in GOAP action names.", OnActions);
    }

    private void OnHelp(string command, string[] args)
    {
        _monitor.Log("LivingTown GOAP commands:", LogLevel.Info);
        _monitor.Log("  lt_goap_help", LogLevel.Info);
        _monitor.Log("  lt_goap_actions", LogLevel.Info);
        _monitor.Log("  lt_goap_state <npc>", LogLevel.Info);
        _monitor.Log("  lt_goap_goal <npc> <GoalKey=Value> [priority] [reason]", LogLevel.Info);
        _monitor.Log("  lt_goap_action <npc> <ActionName>", LogLevel.Info);
    }

    private void OnActions(string command, string[] args)
    {
        foreach (var action in ActionLibrary.GetDefaultActions().OrderBy(a => a.Name))
            _monitor.Log($"  {action.Name}", LogLevel.Info);
    }

    private void OnGoal(string command, string[] args)
    {
        if (args.Length < 2)
        {
            _monitor.Log("Usage: lt_goap_goal <npc> <GoalKey=Value> [priority] [reason]", LogLevel.Warn);
            return;
        }

        var npcName = args[0];
        var goalText = args[1];
        var priority = args.Length >= 3 ? args[2] : null;
        var reason = args.Length >= 4 ? string.Join(' ', args.Skip(3)) : "debug command";
        var goal = GoapCommandParser.BuildGoal(npcName, goalText, priority, reason);

        ExecuteGoal(goal);
    }

    private void OnAction(string command, string[] args)
    {
        if (args.Length < 2)
        {
            _monitor.Log("Usage: lt_goap_action <npc> <ActionName>", LogLevel.Warn);
            return;
        }

        var npcName = args[0];
        var actionName = args[1];
        var action = ActionLibrary.GetDefaultActions()
            .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));

        if (action == null)
        {
            _monitor.Log($"Unknown GOAP action: {actionName}", LogLevel.Warn);
            return;
        }

        _seedNpcState(npcName);
        _executor.ExecuteAction(npcName, action);
        _monitor.Log($"Executed GOAP action for {npcName}: {action.Name}", LogLevel.Info);
    }

    private void OnState(string command, string[] args)
    {
        if (args.Length < 1)
        {
            _monitor.Log("Usage: lt_goap_state <npc>", LogLevel.Warn);
            return;
        }

        var npcName = args[0];
        _seedNpcState(npcName);
        var snapshot = _blackboard.GetNpcSnapshot(npcName);
        if (snapshot.Count == 0)
        {
            _monitor.Log($"No GOAP state found for {npcName}.", LogLevel.Info);
            return;
        }

        _monitor.Log($"GOAP state for {npcName}:", LogLevel.Info);
        foreach (var entry in snapshot.OrderBy(kvp => kvp.Key))
            _monitor.Log($"  {entry.Key} = {entry.Value}", LogLevel.Info);
    }

    private void ExecuteGoal(Goal goal)
    {
        _seedNpcState(goal.NpcName);
        var plan = _planner.PlanFromGoal(_blackboard, goal);
        if (plan.Count == 0)
        {
            _monitor.Log($"No GOAP plan found for {goal.NpcName} -> {goal.GoalKey}={goal.GoalValue}", LogLevel.Warn);
            return;
        }

        _monitor.Log($"GOAP plan for {goal.NpcName}: {string.Join(" -> ", plan.Select(a => a.Name))}", LogLevel.Info);
        _executor.ExecutePlan(goal.NpcName, plan);
    }
}
