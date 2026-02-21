using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// Loads NPC soul definitions from assets/souls/*.json at game startup.
/// Provides soul data as a serialized string for injection into LLM prompts.
/// </summary>
public class SoulLoader
{
    private readonly Dictionary<string, JObject> _souls = new();
    private readonly IMonitor _monitor;

    public SoulLoader(IMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Load all soul files from the given directory.
    /// Call this once during mod initialization.
    /// </summary>
    public void LoadAll(string soulsDirectory)
    {
        if (!System.IO.Directory.Exists(soulsDirectory))
        {
            _monitor.Log($"[SoulLoader] Souls directory not found: {soulsDirectory}", LogLevel.Warn);
            return;
        }

        foreach (var file in System.IO.Directory.GetFiles(soulsDirectory, "*.json"))
        {
            try
            {
                var json = System.IO.File.ReadAllText(file);
                var soul = JObject.Parse(json);
                var name = soul["Name"]?.ToString() ?? System.IO.Path.GetFileNameWithoutExtension(file);
                _souls[name] = soul;
                _monitor.Log($"[SoulLoader] Loaded soul: {name}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _monitor.Log($"[SoulLoader] Failed to load {file}: {ex.Message}", LogLevel.Error);
            }
        }

        _monitor.Log($"[SoulLoader] Total souls loaded: {_souls.Count}", LogLevel.Info);
    }

    /// <summary>Get the soul JObject for an NPC. Returns null if not found.</summary>
    public JObject? GetSoul(string npcName) =>
        _souls.TryGetValue(npcName, out var soul) ? soul : null;

    /// <summary>
    /// Get the soul as a formatted string for prompt injection.
    /// Extracts key fields to keep the prompt concise.
    /// </summary>
    public string GetSoulForPrompt(string npcName)
    {
        var soul = GetSoul(npcName);
        if (soul == null)
            return $"You are {npcName} from Stardew Valley. Stay in character.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Name: {soul["Name"]}");
        sb.AppendLine($"Core Traits: {soul["CoreTraits"]}");
        sb.AppendLine($"Background: {soul["BackgroundSummary"]}");
        sb.AppendLine($"Speech Style: {soul["SpeechStyle"]}");

        if (soul["Relationships"] is JObject rels)
        {
            sb.AppendLine("Key Relationships:");
            foreach (var r in rels.Properties())
                sb.AppendLine($"  - {r.Name}: {r.Value}");
        }

        if (soul["Boundaries"] is JArray bounds)
        {
            sb.AppendLine("Behavioral Boundaries:");
            foreach (var b in bounds)
                sb.AppendLine($"  - {b}");
        }

        if (soul["FourthWallHooks"]?["Triggers"] is JArray triggers)
        {
            sb.AppendLine("Special Topic Reactions:");
            foreach (var t in triggers)
            {
                var keywords = t["Keywords"]?.ToString() ?? "";
                var hint = t["Hint"]?.ToString() ?? "";
                sb.AppendLine($"  - When player mentions [{keywords}]: {hint}");
            }
        }

        if (soul["IntentRecognition"]?["Intents"] is JObject intents)
        {
            sb.AppendLine("Intent-based Behavior Hints:");
            foreach (var i in intents.Properties())
                sb.AppendLine($"  - {i.Name}: {i.Value}");
        }

        return sb.ToString();
    }

    /// <summary>Check if a soul exists for the given NPC.</summary>
    public bool HasSoul(string npcName) => _souls.ContainsKey(npcName);

    /// <summary>Get all loaded NPC names.</summary>
    public IReadOnlyList<string> GetLoadedNpcs() => _souls.Keys.ToList();
}
