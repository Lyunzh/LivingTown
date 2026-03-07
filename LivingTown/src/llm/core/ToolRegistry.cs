using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using System.Threading;

namespace LivingTown.LLM.Core;

public class ToolParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public bool Required { get; set; } = true;
}

public class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ToolParameter> Parameters { get; set; } = new();
    public Func<JObject, CancellationToken, Task<string>> ExecuteAsync { get; set; } = null!;

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

public class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly IMonitor _monitor;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly AsyncLocal<Dictionary<string, string>?> _executionContext = new();

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

    public string? GetContextValue(string key)
    {
        return _executionContext.Value != null && _executionContext.Value.TryGetValue(key, out var value)
            ? value
            : null;
    }

    public IDisposable BeginContextScope(Dictionary<string, string>? context)
    {
        var previous = _executionContext.Value;
        _executionContext.Value = context != null ? new Dictionary<string, string>(context) : null;
        return new Scope(() => _executionContext.Value = previous);
    }

    public JArray ToApiToolsArray() =>
        new(_tools.Values.Select(t => t.ToApiSchema()));

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

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        public Scope(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
