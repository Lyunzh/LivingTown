using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingTown.LLM.Core;

/// <summary>
/// Represents a single message in the LLM conversation history.
/// Supports system, user, assistant, and tool roles.
/// </summary>
public class ChatMessage
{
    [JsonProperty("role")]
    public string Role { get; set; } = "";

    [JsonProperty("content")]
    public string? Content { get; set; }

    /// <summary>Tool calls requested by the assistant.</summary>
    [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
    public List<ToolCallInfo>? ToolCalls { get; set; }

    /// <summary>When role = "tool", this is the ID of the tool call being responded to.</summary>
    [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? ToolCallId { get; set; }

    // ── Factory methods ──

    public static ChatMessage System(string content) => new() { Role = "system", Content = content };
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };

    public static ChatMessage Assistant(string? content, List<ToolCallInfo>? toolCalls = null) =>
        new() { Role = "assistant", Content = content, ToolCalls = toolCalls };

    public static ChatMessage Tool(string toolCallId, string content) =>
        new() { Role = "tool", Content = content, ToolCallId = toolCallId };
}

/// <summary>
/// A single tool call emitted by the LLM in its response.
/// </summary>
public class ToolCallInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("type")]
    public string Type { get; set; } = "function";

    [JsonProperty("function")]
    public ToolCallFunction Function { get; set; } = new();
}

/// <summary>
/// The function name + arguments within a tool call.
/// </summary>
public class ToolCallFunction
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("arguments")]
    public string Arguments { get; set; } = "{}";

    /// <summary>Parse Arguments JSON string into a JObject for tool execution.</summary>
    public JObject ParseArguments() => JObject.Parse(Arguments);
}
