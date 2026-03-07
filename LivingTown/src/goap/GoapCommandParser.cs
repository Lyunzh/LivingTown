using StardewModdingAPI;

namespace LivingTown.GOAP;

public static class GoapCommandParser
{
    public static Goal BuildGoal(string npcName, string goalName, string? priorityText = null, string? reason = null)
    {
        var parts = goalName.Split('=', 2);
        var goalKey = parts[0].Trim();
        var goalValue = parts.Length > 1 ? ParseGoalValue(parts[1].Trim()) : true;

        return new Goal
        {
            NpcName = npcName,
            GoalKey = goalKey,
            GoalValue = goalValue,
            Priority = ParsePriority(priorityText),
            Reason = reason ?? string.Empty
        };
    }

    public static object ParseGoalValue(string raw)
    {
        if (bool.TryParse(raw, out var boolValue))
            return boolValue;
        if (int.TryParse(raw, out var intValue))
            return intValue;
        return raw;
    }

    public static GoalPriority ParsePriority(string? raw)
    {
        return raw?.ToLowerInvariant() switch
        {
            "high" => GoalPriority.High,
            "low" => GoalPriority.Low,
            _ => GoalPriority.Medium
        };
    }
}
