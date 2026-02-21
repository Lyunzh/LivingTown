using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

/// <summary>
/// Configuration for creating an Agent instance.
/// </summary>
public class AgentConfig
{
    /// <summary>The mode name, used to load the system prompt template from PromptManager.</summary>
    public string Mode { get; set; } = "NpcChat";

    /// <summary>Context variables for placeholder injection into the system prompt.</summary>
    public Dictionary<string, string> Context { get; set; } = new();

    /// <summary>Maximum number of ReACT iterations (Thought → Action → Observe cycles).</summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>Optional: override which tools are available. If null, uses all registered tools.</summary>
    public List<string>? AllowedTools { get; set; }
}

/// <summary>
/// The result of an Agent execution.
/// </summary>
public class AgentResult
{
    /// <summary>The final text answer from the agent.</summary>
    public string FinalAnswer { get; set; } = "";

    /// <summary>Number of ReACT iterations consumed.</summary>
    public int IterationsUsed { get; set; }

    /// <summary>Whether the agent hit the max iteration limit without producing a final answer.</summary>
    public bool Truncated { get; set; }

    /// <summary>Any error that occurred during execution.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// ReACT Agent Engine: the core Reason-Act-Observe loop.
/// 
/// This is a SCOPED SERVICE — instantiated on-demand, used once, then discarded.
/// It is NOT a long-lived daemon. The Watchdog or nightly batch creates one when needed.
///
/// Flow:
///   1. Build system prompt from mode + context
///   2. Send user objective to LLM
///   3. If LLM returns tool_calls → execute tools in parallel → feed Observations back
///   4. Repeat step 2-3 until LLM returns a final text answer (no tool_calls)
///   5. Return AgentResult
/// </summary>
public class ReActAgent
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private readonly IMonitor _monitor;
    private readonly PromptManager _promptManager;
    private readonly ToolRegistry _toolRegistry;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    public ReActAgent(
        IMonitor monitor,
        PromptManager promptManager,
        ToolRegistry toolRegistry,
        string apiKey,
        string baseUrl,
        string model = "deepseek-chat")
    {
        _monitor = monitor;
        _promptManager = promptManager;
        _toolRegistry = toolRegistry;
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    /// <summary>
    /// Execute the ReACT loop for the given objective.
    /// This is the main entry point — call it, await it, get a result.
    /// </summary>
    public async Task<AgentResult> RunAsync(string objective, AgentConfig config, CancellationToken ct = default)
    {
        _monitor.Log($"[ReActAgent] Starting mode={config.Mode}, objective=\"{Truncate(objective, 80)}\"", LogLevel.Info);

        // Step 1: Build system prompt and conversation history
        var systemPrompt = _promptManager.BuildSystemPrompt(config.Mode, config.Context);
        var messages = new List<ChatMessage>
        {
            ChatMessage.System(systemPrompt),
            ChatMessage.User(objective)
        };

        // Step 2: Prepare available tools
        var availableTools = BuildToolsArray(config.AllowedTools);

        // Step 3: ReACT loop
        for (int i = 0; i < config.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            _monitor.Log($"[ReActAgent] Iteration {i + 1}/{config.MaxIterations}", LogLevel.Debug);

            // Call LLM
            var response = await CallLlmAsync(messages, availableTools, ct);
            if (response == null)
            {
                return new AgentResult
                {
                    Error = "LLM returned null response",
                    IterationsUsed = i + 1
                };
            }

            // Parse the assistant message
            var assistantMsg = ParseAssistantResponse(response);
            messages.Add(assistantMsg);

            // If no tool calls → we have a final answer
            if (assistantMsg.ToolCalls == null || assistantMsg.ToolCalls.Count == 0)
            {
                _monitor.Log($"[ReActAgent] Final answer after {i + 1} iterations.", LogLevel.Info);
                return new AgentResult
                {
                    FinalAnswer = assistantMsg.Content ?? "",
                    IterationsUsed = i + 1
                };
            }

            // Execute tool calls in parallel
            _monitor.Log($"[ReActAgent] Executing {assistantMsg.ToolCalls.Count} tool call(s)...", LogLevel.Debug);
            var results = await _toolRegistry.ExecuteToolCallsAsync(assistantMsg.ToolCalls, ct);

            // Append tool results as Observation messages
            foreach (var (toolCallId, result) in results)
            {
                messages.Add(ChatMessage.Tool(toolCallId, result));
            }
        }

        // Hit max iterations — force a summary
        _monitor.Log($"[ReActAgent] Max iterations ({config.MaxIterations}) reached. Truncating.", LogLevel.Warn);
        return new AgentResult
        {
            FinalAnswer = messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? "[Agent reached max iterations]",
            IterationsUsed = config.MaxIterations,
            Truncated = true
        };
    }

    // ── Private Helpers ──

    private JArray? BuildToolsArray(List<string>? allowedTools)
    {
        var allTools = _toolRegistry.GetAll();
        if (allTools.Count == 0) return null;

        if (allowedTools != null)
        {
            allTools = allTools.Where(t => allowedTools.Contains(t.Name)).ToList();
        }

        if (allTools.Count == 0) return null;
        return new JArray(allTools.Select(t => t.ToApiSchema()));
    }

    private async Task<JObject?> CallLlmAsync(List<ChatMessage> messages, JArray? tools, CancellationToken ct)
    {
        var requestBody = new JObject
        {
            ["model"] = _model,
            ["messages"] = JArray.FromObject(messages, JsonSerializer.Create(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }))
        };

        if (tools != null && tools.Count > 0)
        {
            requestBody["tools"] = tools;
        }

        var json = requestBody.ToString(Formatting.None);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            var response = await Http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _monitor.Log($"[ReActAgent] LLM API error {response.StatusCode}: {Truncate(body, 200)}", LogLevel.Error);
                return null;
            }

            return JObject.Parse(body);
        }
        catch (Exception ex)
        {
            _monitor.Log($"[ReActAgent] HTTP error: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    private ChatMessage ParseAssistantResponse(JObject response)
    {
        var choice = response["choices"]?[0]?["message"];
        if (choice == null)
        {
            return ChatMessage.Assistant("[No response from LLM]");
        }

        var content = choice["content"]?.ToString();
        List<ToolCallInfo>? toolCalls = null;

        var toolCallsToken = choice["tool_calls"];
        if (toolCallsToken is JArray toolCallsArray && toolCallsArray.Count > 0)
        {
            toolCalls = toolCallsArray.ToObject<List<ToolCallInfo>>();
        }

        return ChatMessage.Assistant(content, toolCalls);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
