using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

/// <summary>
/// Describes a tool's parameter in JSON Schema format for the LLM function-calling API.
/// </summary>
public class ToolParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public bool Required { get; set; } = true;
}

/// <summary>
/// A registered tool definition and its execution delegate.
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ToolParameter> Parameters { get; set; } = new();

    /// <summary>
    /// The async function to execute when the LLM calls this tool.
    /// Input: parsed JSON arguments. Output: string result for the LLM's Observation.
    /// </summary>
    public Func<JObject, CancellationToken, Task<string>> ExecuteAsync { get; set; } = null!;

    /// <summary>
    /// Serialize this tool definition into the OpenAI function-calling JSON schema.
    /// </summary>
    public JObject ToApiSchema()
    {
        var properties = new JObject();
        var required = new JArray();

        foreach (var p in Parameters)
        {
            properties[p.Name] = new JObject
            {
                ["type"] = p.Type,
                ["description"] = p.Description
            };
            if (p.Required)
                required.Add(p.Name);
        }

        return new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = Name,
                ["description"] = Description,
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required
                }
            }
        };
    }
}

/// <summary>
/// Central registry for all tools available to agents.
/// Supports parallel execution of multiple tool calls via Task.WhenAll + SemaphoreSlim.
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly IMonitor _monitor;

    /// <summary>Global concurrency limiter for external API calls (e.g., max 2 concurrent HTTP requests).</summary>
    private readonly SemaphoreSlim _concurrencyLimiter;

    public ToolRegistry(IMonitor monitor, int maxConcurrency = 3)
    {
        _monitor = monitor;
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public void Register(ToolDefinition tool)
    {
        _tools[tool.Name] = tool;
        _monitor.Log($"[ToolRegistry] Registered tool: {tool.Name}", LogLevel.Debug);
    }

    public ToolDefinition? Get(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    public IReadOnlyList<ToolDefinition> GetAll() => _tools.Values.ToList();

    /// <summary>
    /// Generate the "tools" array for the LLM API request body.
    /// </summary>
    public JArray ToApiToolsArray() =>
        new(_tools.Values.Select(t => t.ToApiSchema()));

    /// <summary>
    /// Execute multiple tool calls in parallel with concurrency limiting.
    /// Returns a list of (toolCallId, result) pairs.
    /// </summary>
    public async Task<List<(string ToolCallId, string Result)>> ExecuteToolCallsAsync(
        List<ToolCallInfo> toolCalls, CancellationToken ct)
    {
        var tasks = toolCalls.Select(async call =>
        {
            await _concurrencyLimiter.WaitAsync(ct);
            try
            {
                var tool = Get(call.Function.Name);
                if (tool == null)
                {
                    _monitor.Log($"[ToolRegistry] Unknown tool: {call.Function.Name}", LogLevel.Warn);
                    return (call.Id, $"Error: Unknown tool '{call.Function.Name}'");
                }

                _monitor.Log($"[ToolRegistry] Executing tool: {call.Function.Name}", LogLevel.Debug);

                try
                {
                    var args = call.Function.ParseArguments();
                    var result = await tool.ExecuteAsync(args, ct);
                    return (call.Id, result);
                }
                catch (Exception ex)
                {
                    _monitor.Log($"[ToolRegistry] Tool '{call.Function.Name}' failed: {ex.Message}", LogLevel.Error);
                    return (call.Id, $"Error executing tool: {ex.Message}");
                }
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
