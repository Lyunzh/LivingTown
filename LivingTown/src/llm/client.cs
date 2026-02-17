using System.ClientModel;
using System.Runtime.CompilerServices;
using LivingTown.Pipeline;
using Microsoft.Extensions.AI;
using OpenAI;
using StardewModdingAPI;

namespace LivingTown.LLM;

/// <summary>
/// LLM client interface. Returns an IAsyncEnumerable for streaming.
/// </summary>
public interface ILLMClient
{
    /// <summary>
    /// Generate a streaming response. Returns an IAsyncEnumerable of text tokens.
    /// The caller consumes the stream at its own pace.
    /// </summary>
    IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        string prompt, CancellationToken ct = default);
}

/// <summary>
/// Concrete LLM client using Microsoft.Extensions.AI + OpenAI SDK.
/// Connects to DeepSeek via OpenAI-compatible API.
/// One instance per session, holding conversation context.
/// </summary>
public class LLMClient : ILLMClient
{
    private readonly IChatClient _chatClient;
    private readonly List<ChatMessage> _messages = new();
    private readonly IMonitor _monitor;

    public LLMClient(Session session, IMonitor monitor)
    {
        _monitor = monitor;

        // Load .env
        var (apiKey, baseUrl) = LoadConfig();

        // Create OpenAI client pointing to DeepSeek endpoint
        var credential = new ApiKeyCredential(apiKey ?? "");
        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var openAiClient = new OpenAIClient(credential, options);

        // Get IChatClient via Microsoft.Extensions.AI
        _chatClient = openAiClient
            .GetChatClient("deepseek-chat")
            .AsIChatClient();

        // System prompt
        _messages.Add(new ChatMessage(ChatRole.System,
            $"You are {session.NpcName} from Stardew Valley. " +
            "Stay in character. Respond naturally and keep responses concise (1-3 sentences). " +
            "Speak as the character would in the game."));
    }

    public async IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        string prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Add user message to conversation history
        _messages.Add(new ChatMessage(ChatRole.User, prompt));

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(_messages, null, ct))
        {
            if (update.Text is { Length: > 0 } text)
            {
                fullResponse.Append(text);
                yield return text;
            }
        }

        // Add completed assistant response to history
        _messages.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
    }

    private static (string? apiKey, string baseUrl) LoadConfig()
    {
        string? apiKey = null;
        string baseUrl = "https://api.deepseek.com/v1";

        try
        {
            // Find .env relative to the mod's DLL location or project root
            var assemblyDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

            // Walk up to find .env
            var searchDir = assemblyDir;
            for (int i = 0; i < 5; i++)
            {
                var envPath = System.IO.Path.Combine(searchDir, ".env");
                if (System.IO.File.Exists(envPath))
                {
                    foreach (var line in System.IO.File.ReadAllLines(envPath))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("DEEPSEEK_API_KEY="))
                            apiKey = trimmed["DEEPSEEK_API_KEY=".Length..].Trim();
                        if (trimmed.StartsWith("DEEPSEEK_BASE_URL="))
                            baseUrl = trimmed["DEEPSEEK_BASE_URL=".Length..].Trim();
                    }
                    break;
                }
                searchDir = System.IO.Path.GetDirectoryName(searchDir) ?? "";
            }
        }
        catch { /* Config loading should never crash the mod */ }

        // Ensure base URL has /v1 suffix for OpenAI-compatible endpoint
        if (!baseUrl.EndsWith("/v1"))
            baseUrl = baseUrl.TrimEnd('/') + "/v1";

        return (apiKey, baseUrl);
    }
}