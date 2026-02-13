using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StardewModdingAPI;
using System.IO;

namespace LivingTown
{
    public class LLMService
    {
        private readonly IMonitor _monitor;
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _baseUrl;

        public LLMService(IMonitor monitor)
        {
            _monitor = monitor;
            _httpClient = new HttpClient();
            LoadConfig();
        }

        private void LoadConfig()
        {
            // Simplified loading from .env for now, ideally use SMAPI config
            // In a real scenario, we might want to parse the .env file if it exists in the mod folder
            // or just hardcode for this specific user request environment if they provided it in project root.
            // For this specific user context, the .env is at project root. 
            // However, a compiled mod won't see the project root .env easily.
            // Let's assume for now we can read it or the user puts it in the mod folder.
            // For Development, we'll try to read from the path we saw earlier: c:\Users\23563\RiderProjects\LivingTown\.env
            
            try
            {
                string envPath = Path.Combine("c:/Users/23563/RiderProjects/LivingTown", ".env");
                if (File.Exists(envPath))
                {
                    var lines = File.ReadAllLines(envPath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("DEEPSEEK_API_KEY="))
                            _apiKey = line.Substring("DEEPSEEK_API_KEY=".Length).Trim();
                        if (line.StartsWith("DEEPSEEK_BASE_URL="))
                            _baseUrl = line.Substring("DEEPSEEK_BASE_URL=".Length).Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _monitor.Log($"Failed to load .env: {ex.Message}", LogLevel.Error);
            }

            if (string.IsNullOrEmpty(_baseUrl)) _baseUrl = "https://api.deepseek.com";
        }

        public async IAsyncEnumerable<string> GetDialogueStreamAsync(string npcName, string playerPrompt)
        {
             if (string.IsNullOrEmpty(_apiKey))
            {
                yield return "[Error: API Key missing]";
                yield break;
            }

            var requestBody = new
            {
                model = "deepseek-chat", // Or appropriate model name
                messages = new[]
                {
                    new { role = "system", content = $"You are {npcName} from Stardew Valley. Keep responses short and in character." },
                    new { role = "user", content = playerPrompt }
                },
                stream = true
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = content;

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                yield return $"[Error: {response.StatusCode}]";
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("data: [DONE]")) break;
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring("data: ".Length);
                    dynamic jsonResponse = JsonConvert.DeserializeObject(data);
                    string delta = jsonResponse?.choices?[0]?.delta?.content;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        yield return delta;
                    }
                }
            }
        }
    }
}
