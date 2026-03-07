using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM.Core;

public class AgentConfig
{
    public string Mode { get; set; } = "NpcChat";
    public Dictionary<string, string> Context { get; set; } = new();
    public int MaxIterations { get; set; } = 10;
    public List<string>? AllowedTools { get; set; }
}

public class AgentResult
{
    public string FinalAnswer { get; set; } = "";
    public int IterationsUsed { get; set; }
    public bool Truncated { get; set; }
    public string? Error { get; set; }
}

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

    public async Task<AgentResult> RunAsync(string objective, AgentConfig config, CancellationToken ct = default)
    {
        _monitor.Log($"[ReActAgent] Starting mode={config.Mode}, objective=\"{Truncate(objective, 80)}\"", LogLevel.Info);

        var systemPrompt = _promptManager.BuildSystemPrompt(config.Mode, config.Context);
        var messages = new List<ChatMessage>
        {
            ChatMessage.System(systemPrompt),
            ChatMessage.User(objective)
        };

        var availableTools = BuildToolsArray(config.AllowedTools);

        for (int i = 0; i < config.MaxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            _monitor.Log($"[ReActAgent] Iteration {i + 1}/{config.MaxIterations}", LogLevel.Debug);

            var response = await CallLlmAsync(messages, availableTools, ct);
            if (response == null)
            {
                return new AgentResult
                {
                    Error = "LLM returned null response",
                    IterationsUsed = i + 1
                };
            }

            var assistantMsg = ParseAssistantResponse(response);
            messages.Add(assistantMsg);

            if (assistantMsg.ToolCalls == null || assistantMsg.ToolCalls.Count == 0)
            {
                _monitor.Log($"[ReActAgent] Final answer after {i + 1} iterations.", LogLevel.Info);
                return new AgentResult
                {
                    FinalAnswer = assistantMsg.Content ?? "",
                    IterationsUsed = i + 1
                };
            }

            _monitor.Log($"[ReActAgent] Executing {assistantMsg.ToolCalls.Count} tool call(s)...", LogLevel.Debug);
            using var scope = _toolRegistry.BeginContextScope(config.Context);
            var results = await _toolRegistry.ExecuteToolCallsAsync(assistantMsg.ToolCalls, ct);

            foreach (var (toolCallId, result) in results)
            {
                messages.Add(ChatMessage.Tool(toolCallId, result));
            }
        }

        _monitor.Log($"[ReActAgent] Max iterations ({config.MaxIterations}) reached. Truncating.", LogLevel.Warn);
        return new AgentResult
        {
            FinalAnswer = messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? "[Agent reached max iterations]",
            IterationsUsed = config.MaxIterations,
            Truncated = true
        };
    }

    private JArray? BuildToolsArray(List<string>? allowedTools)
    {
        var allTools = _toolRegistry.GetAll();
        if (allTools.Count == 0) return null;

        if (allowedTools != null)
            allTools = allTools.Where(t => allowedTools.Contains(t.Name)).ToList();

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
            requestBody["tools"] = tools;

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
            return ChatMessage.Assistant("[No response from LLM]");

        var content = choice["content"]?.ToString();
        List<ToolCallInfo>? toolCalls = null;

        var toolCallsToken = choice["tool_calls"];
        if (toolCallsToken is JArray toolCallsArray && toolCallsArray.Count > 0)
            toolCalls = toolCallsArray.ToObject<List<ToolCallInfo>>();

        return ChatMessage.Assistant(content, toolCalls);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
