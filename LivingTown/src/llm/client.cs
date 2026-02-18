using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using LivingTown.Pipeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.LLM;

/// <summary>
/// LLM client interface. Returns an IAsyncEnumerable for streaming.
/// </summary>
public interface ILLMClient
{
    IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        string prompt, CancellationToken ct = default);
}

/// <summary>
/// LLM client using raw HttpClient + SSE (Server-Sent Events) for streaming.
/// Connects to DeepSeek via OpenAI-compatible chat/completions API.
/// One instance per session, holding conversation context.
/// </summary>
public class LLMClient : ILLMClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly List<object> _messages = new();
    private readonly IMonitor _monitor;

    public LLMClient(Session session, IMonitor monitor, string modDir)
    {
        _monitor = monitor;

        var (apiKey, baseUrl) = LoadConfig(modDir, monitor);
        _apiKey = apiKey ?? "";
        _baseUrl = baseUrl;
        _model = "deepseek-chat";

        // System prompt
        _messages.Add(new
        {
            role = "system",
            content = $"You are {session.NpcName} from Stardew Valley. " +
                      "Stay in character. Respond naturally and keep responses concise (1-3 sentences). " +
                      "Speak as the character would in the game."
        });

        _monitor.Log($"[LLMClient] Created for {session.NpcName}, API: {_baseUrl}", LogLevel.Info);
    }

    public async IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        string prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Add user message
        _messages.Add(new { role = "user", content = prompt });
        _monitor.Log($"[LLMClient] Sending prompt ({_messages.Count} msgs): \"{prompt}\"", LogLevel.Debug);

        var fullResponse = new StringBuilder();

        // Build request
        var requestBody = JsonConvert.SerializeObject(new
        {
            model = _model,
            messages = _messages,
            stream = true
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // Send with streaming
        HttpResponseMessage? response = null;
        string? httpError = null;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _monitor.Log($"[LLMClient] HTTP error: {ex.Message}", LogLevel.Error);
            httpError = ex.Message;
        }

        if (httpError != null)
        {
            yield return $"[Error: {httpError}]";
            yield break;
        }

        _monitor.Log("[LLMClient] Stream started, reading SSE...", LogLevel.Debug);

        // Parse SSE stream
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            // SSE format: "data: {...}" or "data: [DONE]"
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            // Parse the JSON chunk
            string? token = null;
            try
            {
                var json = JObject.Parse(data);
                token = json["choices"]?[0]?["delta"]?["content"]?.ToString();
            }
            catch (Exception ex)
            {
                _monitor.Log($"[LLMClient] JSON parse error: {ex.Message}", LogLevel.Trace);
            }

            if (!string.IsNullOrEmpty(token))
            {
                fullResponse.Append(token);
                yield return token;
            }
        }

        // Save assistant response to history
        var result = fullResponse.ToString();
        _messages.Add(new { role = "assistant", content = result });
        _monitor.Log($"[LLMClient] Stream done. Total: {result.Length} chars", LogLevel.Debug);
    }

    private static (string? apiKey, string baseUrl) LoadConfig(string modDir, IMonitor monitor)
    {
        string? apiKey = null;
        string baseUrl = "https://api.deepseek.com";

        // Search locations in priority order:
        // 1. Mod directory (where .env should be deployed)
        // 2. Walk up from DLL location (fallback for dev)
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

            monitor.Log($"[LLMClient] Loading .env from: {envPath}", LogLevel.Info);
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
                monitor.Log($"[LLMClient] Failed to read .env: {ex.Message}", LogLevel.Warn);
            }
            break;
        }

        if (apiKey == null)
            monitor.Log("[LLMClient] WARNING: DEEPSEEK_API_KEY not found in any .env!", LogLevel.Warn);

        return (apiKey, baseUrl.TrimEnd('/'));
    }
}