using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

/// <summary>
/// Built-in tools that ship with the Multi-Agent engine.
/// These are system-level tools available across all modes.
/// </summary>
public static class BuiltinTools
{
    /// <summary>
    /// Registers the "new_task" tool, which spawns a SubAgent with a different mode.
    /// The parent agent blocks until the child agent completes and returns its final answer.
    /// </summary>
    public static ToolDefinition NewTask(
        IMonitor monitor,
        PromptManager promptManager,
        ToolRegistry toolRegistry,
        string apiKey,
        string baseUrl,
        string model = "deepseek-chat")
    {
        return new ToolDefinition
        {
            Name = "new_task",
            Description = "Spawn a sub-agent with a specific mode to handle a sub-task. " +
                          "The sub-agent will execute autonomously and return its final answer. " +
                          "Use this when the current task requires a different expertise or mode.",
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "mode",
                    Type = "string",
                    Description = "The mode/persona for the sub-agent (e.g., 'PersonaBuilder', 'MemoryCompactor').",
                    Required = true
                },
                new()
                {
                    Name = "objective",
                    Type = "string",
                    Description = "The specific task or question for the sub-agent to accomplish.",
                    Required = true
                },
                new()
                {
                    Name = "context",
                    Type = "string",
                    Description = "Optional additional context or data to pass to the sub-agent's prompt.",
                    Required = false
                }
            },
            ExecuteAsync = async (args, ct) =>
            {
                var mode = args["mode"]?.ToString() ?? "NpcChat";
                var objective = args["objective"]?.ToString() ?? "";
                var extraContext = args["context"]?.ToString();

                monitor.Log($"[new_task] Spawning sub-agent: mode={mode}", LogLevel.Info);

                if (!promptManager.HasMode(mode))
                {
                    return $"Error: Unknown mode '{mode}'. Available modes: {string.Join(", ", promptManager.GetModes())}";
                }

                var subAgent = new ReActAgent(monitor, promptManager, toolRegistry, apiKey, baseUrl, model);
                var config = new AgentConfig
                {
                    Mode = mode,
                    MaxIterations = 5 // Sub-agents get a tighter iteration budget
                };

                if (!string.IsNullOrEmpty(extraContext))
                {
                    config.Context["CONTEXT"] = extraContext;
                }

                try
                {
                    var result = await subAgent.RunAsync(objective, config, ct);

                    if (result.Error != null)
                    {
                        monitor.Log($"[new_task] Sub-agent error: {result.Error}", LogLevel.Warn);
                        return $"Sub-agent encountered an error: {result.Error}";
                    }

                    monitor.Log($"[new_task] Sub-agent completed in {result.IterationsUsed} iterations.", LogLevel.Info);
                    return result.FinalAnswer;
                }
                catch (OperationCanceledException)
                {
                    return "Sub-agent was cancelled.";
                }
                catch (Exception ex)
                {
                    monitor.Log($"[new_task] Sub-agent crashed: {ex.Message}", LogLevel.Error);
                    return $"Sub-agent crashed: {ex.Message}";
                }
            }
        };
    }

    /// <summary>
    /// A simple "final_answer" tool that allows the agent to explicitly signal
    /// that it has finished reasoning and wants to return a result.
    /// This is useful for modes where the output format is structured (e.g., JSON).
    /// </summary>
    public static ToolDefinition FinalAnswer()
    {
        return new ToolDefinition
        {
            Name = "final_answer",
            Description = "Use this tool to submit your final answer when you have completed the task. " +
                          "Pass the complete answer as the 'answer' parameter.",
            Parameters = new List<ToolParameter>
            {
                new()
                {
                    Name = "answer",
                    Type = "string",
                    Description = "The final answer or result to return.",
                    Required = true
                }
            },
            ExecuteAsync = (args, _) =>
            {
                // This tool is a signal — the ReACT loop can check for it
                // and short-circuit. For now, just return the answer as-is.
                var answer = args["answer"]?.ToString() ?? "";
                return Task.FromResult(answer);
            }
        };
    }
}
