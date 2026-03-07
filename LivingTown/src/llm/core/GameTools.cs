using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

public static class GameTools
{
    public static ToolDefinition SetGoal(IMonitor monitor, ToolRegistry toolRegistry, GOAP.Blackboard? blackboard = null)
    {
        return new ToolDefinition
        {
            Name = "set_goal",
            Description = "Set a behavioral goal for the NPC. The GOAP planner will figure out a valid action sequence to achieve this goal.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "goal_name", Type = "string", Description = "The goal state to achieve (e.g., 'IsHungry=false').", Required = true },
                new() { Name = "priority", Type = "string", Description = "Priority level: low, medium, or high.", Required = false },
                new() { Name = "reason", Type = "string", Description = "Brief explanation of why this goal was chosen.", Required = false }
            },
            ExecuteAsync = (args, ct) =>
            {
                var npcName = toolRegistry.GetContextValue("NPC_NAME") ?? "UnknownNPC";
                var goalName = args["goal_name"]?.ToString() ?? "";
                var priority = args["priority"]?.ToString() ?? "medium";
                var reason = args["reason"]?.ToString() ?? "";

                monitor.Log($"[GameTools] set_goal: {npcName} -> {goalName} (priority={priority}, reason={reason})", LogLevel.Info);

                var parts = goalName.Split('=', 2);
                var goalKey = parts[0].Trim();
                object goalValue = parts.Length > 1 ? ParseGoalValue(parts[1].Trim()) : true;

                var goalPriority = priority.ToLowerInvariant() switch
                {
                    "high" => GOAP.GoalPriority.High,
                    "low" => GOAP.GoalPriority.Low,
                    _ => GOAP.GoalPriority.Medium
                };

                blackboard?.EnqueueGoal(new GOAP.Goal
                {
                    NpcName = npcName,
                    GoalKey = goalKey,
                    GoalValue = goalValue,
                    Priority = goalPriority,
                    Reason = reason
                });

                return Task.FromResult($"Goal '{goalKey}={goalValue}' has been set for {npcName} with {priority} priority.");
            }
        };
    }

    private static object ParseGoalValue(string s)
    {
        if (bool.TryParse(s, out var b)) return b;
        if (int.TryParse(s, out var i)) return i;
        return s;
    }

    public static ToolDefinition PlayEmote(IMonitor monitor, ToolRegistry toolRegistry, Action<string, int> enqueueEmote)
    {
        return new ToolDefinition
        {
            Name = "play_emote",
            Description = "Play an emote animation above the NPC's head.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "emote", Type = "string", Description = "happy, sad, angry, love, surprised, thinking.", Required = true }
            },
            ExecuteAsync = (args, ct) =>
            {
                var npcName = toolRegistry.GetContextValue("NPC_NAME") ?? "UnknownNPC";
                var emoteName = args["emote"]?.ToString()?.ToLowerInvariant() ?? "happy";
                var emoteId = emoteName switch
                {
                    "happy" => 32,
                    "sad" => 28,
                    "angry" => 12,
                    "love" or "heart" => 20,
                    "surprised" or "exclamation" => 16,
                    "thinking" or "question" => 8,
                    _ => 32
                };

                monitor.Log($"[GameTools] play_emote: {npcName} -> {emoteName} (id={emoteId})", LogLevel.Debug);
                enqueueEmote(npcName, emoteId);
                return Task.FromResult($"Played '{emoteName}' emote for {npcName}.");
            }
        };
    }

    public static ToolDefinition WebSearch(IMonitor monitor)
    {
        return new ToolDefinition
        {
            Name = "web_search",
            Description = "Search the web for information.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "query", Type = "string", Description = "The search query to look up.", Required = true }
            },
            ExecuteAsync = (args, ct) =>
            {
                var query = args["query"]?.ToString() ?? "";
                monitor.Log($"[GameTools] web_search: \"{query}\"", LogLevel.Info);
                return Task.FromResult($"[WebSearch stub] No results for: {query}.");
            }
        };
    }

    public static ToolDefinition RememberFact(IMonitor monitor, ToolRegistry toolRegistry, Action<string, string, int> recordMemory)
    {
        return new ToolDefinition
        {
            Name = "remember",
            Description = "Store an important fact in long-term memory.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "fact", Type = "string", Description = "The fact to remember.", Required = true },
                new() { Name = "importance", Type = "string", Description = "Importance level 1-10.", Required = false }
            },
            ExecuteAsync = (args, ct) =>
            {
                var npcName = toolRegistry.GetContextValue("NPC_NAME") ?? "UnknownNPC";
                var fact = args["fact"]?.ToString() ?? "";
                var importanceStr = args["importance"]?.ToString() ?? "5";
                int.TryParse(importanceStr, out var importance);
                importance = Math.Clamp(importance, 1, 10);

                monitor.Log($"[GameTools] remember: {npcName} -> \"{fact}\" (importance={importance})", LogLevel.Debug);
                recordMemory(npcName, fact, importance);
                return Task.FromResult($"Remembered for {npcName}: \"{fact}\" (importance={importance}/10)");
            }
        };
    }
}
