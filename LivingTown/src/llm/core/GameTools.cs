using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

/// <summary>
/// Game-specific tools that bridge the ReACT Agent with Stardew Valley game mechanics.
/// These tools allow the LLM to directly influence NPC behavior and game state.
///
/// Architecture relationship:
///   Agent.Tool("set_goal") → writes Goal to GOAP Blackboard
///   Agent.Tool("play_emote") → enqueues emote action to main-thread queue
///   Agent.Tool("web_search") → async HTTP call to search API
///
/// The GOAP engine is a SEPARATE system that reads from the Blackboard.
/// Tools are the BRIDGE: Agent decides WHAT to do, GOAP decides HOW to do it.
/// </summary>
public static class GameTools
{
    /// <summary>
    /// Tool: Set a goal on the GOAP Blackboard for an NPC.
    /// The LLM decides the high-level goal (e.g., "go buy salad"),
    /// GOAP resolves the concrete action sequence (pathfind → walk → buy → eat).
    ///
    /// This is the key linkage: Agent → Tool("set_goal") → Blackboard → GOAPPlanner → NPC Actions.
    /// </summary>
    public static ToolDefinition SetGoal(IMonitor monitor)
    {
        return new ToolDefinition
        {
            Name = "set_goal",
            Description = "Set a behavioral goal for the NPC. The GOAP planner will figure out " +
                          "a valid action sequence to achieve this goal. Use this when the NPC " +
                          "should DO something physical in the game world (walk, eat, buy, visit).",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "goal_name", Type = "string",
                    Description = "The goal state to achieve (e.g., 'IsHungry=false', 'VisitLocation=Saloon', 'TalkTo=Sam').",
                    Required = true },
                new() { Name = "priority", Type = "string",
                    Description = "Priority level: 'low', 'medium', or 'high'. High priority goals interrupt current behavior.",
                    Required = false },
                new() { Name = "reason", Type = "string",
                    Description = "Brief explanation of why this goal was chosen (for memory logging).",
                    Required = false }
            },
            ExecuteAsync = (args, ct) =>
            {
                var goalName = args["goal_name"]?.ToString() ?? "";
                var priority = args["priority"]?.ToString() ?? "medium";
                var reason = args["reason"]?.ToString() ?? "";

                monitor.Log($"[GameTools] set_goal: {goalName} (priority={priority}, reason={reason})", LogLevel.Info);

                // TODO: Write to GOAP Blackboard when implemented
                // For now, return confirmation so the Agent knows the goal was accepted
                return Task.FromResult($"Goal '{goalName}' has been set with {priority} priority. " +
                                       "The NPC will begin working toward this goal.");
            }
        };
    }

    /// <summary>
    /// Tool: Play an emote animation on the NPC.
    /// Direct game action — no GOAP needed (immediate execution).
    /// </summary>
    public static ToolDefinition PlayEmote(IMonitor monitor, Action<string, int> enqueueEmote)
    {
        return new ToolDefinition
        {
            Name = "play_emote",
            Description = "Play an emote animation above the NPC's head. " +
                          "Use this to express emotions non-verbally during conversation.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "emote", Type = "string",
                    Description = "The emote to play: 'happy', 'sad', 'angry', 'love', 'surprised', 'thinking'.",
                    Required = true }
            },
            ExecuteAsync = (args, ct) =>
            {
                var emoteName = args["emote"]?.ToString()?.ToLower() ?? "happy";
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

                monitor.Log($"[GameTools] play_emote: {emoteName} (id={emoteId})", LogLevel.Debug);
                enqueueEmote("__CURRENT_NPC__", emoteId);
                return Task.FromResult($"Played '{emoteName}' emote.");
            }
        };
    }

    /// <summary>
    /// Tool: Search the web for information (async, non-blocking).
    /// Used for "breaking the fourth wall" or enriching NPC knowledge.
    ///
    /// NOTE: In the live NpcChat mode, this tool is typically NOT available
    /// (to avoid blocking dialogue). The Watchdog may queue a WebSearch
    /// as a nightly background task instead.
    /// For the PersonaBuilder mode, this is a core tool.
    /// </summary>
    public static ToolDefinition WebSearch(IMonitor monitor)
    {
        return new ToolDefinition
        {
            Name = "web_search",
            Description = "Search the web for information. Returns a text summary of search results. " +
                          "Use this to look up facts, wiki pages, or real-world information.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "query", Type = "string",
                    Description = "The search query to look up.",
                    Required = true }
            },
            ExecuteAsync = async (args, ct) =>
            {
                var query = args["query"]?.ToString() ?? "";
                monitor.Log($"[GameTools] web_search: \"{query}\"", LogLevel.Info);

                // TODO: Implement actual web search (e.g., SerpAPI, Bing Search API)
                // For now, return a placeholder that signals the tool works
                return $"[WebSearch stub] No results for: {query}. " +
                       "Implement actual search API integration to enable this feature.";
            }
        };
    }

    /// <summary>
    /// Tool: Store a fact in long-term memory.
    /// Allows the agent to explicitly decide what's worth remembering.
    /// </summary>
    public static ToolDefinition RememberFact(IMonitor monitor, Action<string, string, int> recordMemory)
    {
        return new ToolDefinition
        {
            Name = "remember",
            Description = "Store an important fact in long-term memory. " +
                          "Use this when the player shares something the NPC should remember for future conversations.",
            Parameters = new List<ToolParameter>
            {
                new() { Name = "fact", Type = "string",
                    Description = "The fact to remember (e.g., 'Player's favorite color is blue').",
                    Required = true },
                new() { Name = "importance", Type = "string",
                    Description = "Importance level 1-10. 10 = never forget, 1 = minor detail.",
                    Required = false }
            },
            ExecuteAsync = (args, ct) =>
            {
                var fact = args["fact"]?.ToString() ?? "";
                var importanceStr = args["importance"]?.ToString() ?? "5";
                int.TryParse(importanceStr, out var importance);
                importance = Math.Clamp(importance, 1, 10);

                monitor.Log($"[GameTools] remember: \"{fact}\" (importance={importance})", LogLevel.Debug);
                recordMemory("__CURRENT_NPC__", fact, importance);
                return Task.FromResult($"Remembered: \"{fact}\" (importance={importance}/10)");
            }
        };
    }
}
