using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

/// <summary>
/// Convenience factory for creating pre-configured ReActAgent instances.
/// This is the primary entry point used by the Watchdog (L1) and Nightly Batch (L2).
///
/// Usage:
///   var factory = new AgentFactory(monitor, modDir);
///   var result = await factory.RunAsync("NpcChat", "What do you think about the rain?",
///       new() { ["NPC_NAME"] = "Abigail", ["SOUL"] = soulJson, ["MEMORIES"] = memories });
/// </summary>
public class AgentFactory
{
    private readonly IMonitor _monitor;
    private readonly PromptManager _promptManager;
    private readonly ToolRegistry _toolRegistry;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    public AgentFactory(IMonitor monitor, string modDir, string model = "deepseek-chat")
    {
        _monitor = monitor;
        _promptManager = PromptManager.CreateWithDefaults();
        _toolRegistry = new ToolRegistry(monitor, maxConcurrency: 2);
        _model = model;

        // Load API config
        var (apiKey, baseUrl) = LoadConfig(modDir);
        _apiKey = apiKey ?? "";
        _baseUrl = baseUrl;

        // Register built-in tools
        _toolRegistry.Register(BuiltinTools.NewTask(monitor, _promptManager, _toolRegistry, _apiKey, _baseUrl, _model));
        _toolRegistry.Register(BuiltinTools.FinalAnswer());

        _monitor.Log("[AgentFactory] Initialized with built-in tools and default modes.", LogLevel.Info);
    }

    /// <summary>Access the PromptManager to register custom modes.</summary>
    public PromptManager PromptManager => _promptManager;

    /// <summary>Access the ToolRegistry to register custom tools.</summary>
    public ToolRegistry ToolRegistry => _toolRegistry;

    /// <summary>
    /// Create and execute a one-shot agent with the specified mode and objective.
    /// This is the simplest way to use the multi-agent system.
    /// </summary>
    public Task<AgentResult> RunAsync(
        string mode,
        string objective,
        Dictionary<string, string>? context = null,
        int maxIterations = 10,
        CancellationToken ct = default)
    {
        var agent = new ReActAgent(_monitor, _promptManager, _toolRegistry, _apiKey, _baseUrl, _model);
        var config = new AgentConfig
        {
            Mode = mode,
            MaxIterations = maxIterations,
            Context = context ?? new()
        };

        return agent.RunAsync(objective, config, ct);
    }

    /// <summary>
    /// Create a raw ReActAgent instance for advanced usage
    /// (e.g., when you need to configure AllowedTools or reuse the agent).
    /// </summary>
    public ReActAgent CreateAgent() =>
        new(_monitor, _promptManager, _toolRegistry, _apiKey, _baseUrl, _model);

    // ── Config Loader (reused from the old LLMClient) ──

    private (string? apiKey, string baseUrl) LoadConfig(string modDir)
    {
        string? apiKey = null;
        string baseUrl = "https://api.deepseek.com";

        var searchPaths = new List<string> { modDir };
        var assemblyDir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        var dir = assemblyDir;
        for (int i = 0; i < 6; i++)
        {
            searchPaths.Add(dir);
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }

        foreach (var searchDir in searchPaths)
        {
            var envPath = System.IO.Path.Combine(searchDir, ".env");
            if (!System.IO.File.Exists(envPath)) continue;

            _monitor.Log($"[AgentFactory] Loading .env from: {envPath}", LogLevel.Info);
            try
            {
                foreach (var line in System.IO.File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DEEPSEEK_API_KEY="))
                        apiKey = trimmed["DEEPSEEK_API_KEY=".Length..].Trim();
                    if (trimmed.StartsWith("DEEPSEEK_BASE_URL="))
                        baseUrl = trimmed["DEEPSEEK_BASE_URL=".Length..].Trim();
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"[AgentFactory] Failed to read .env: {ex.Message}", LogLevel.Warn);
            }
            break;
        }

        if (apiKey == null)
            _monitor.Log("[AgentFactory] WARNING: DEEPSEEK_API_KEY not found!", LogLevel.Warn);

        return (apiKey, baseUrl.TrimEnd('/'));
    }
}
