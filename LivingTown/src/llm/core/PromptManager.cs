namespace LivingTown.LLM.Core;

/// <summary>
/// Manages mode-specific system prompts.
/// Each "mode" is a named template that defines the Agent's persona and behavioral constraints.
/// Supports dynamic placeholder injection (e.g., {NPC_NAME}, {CURRENT_TIME}).
/// </summary>
public class PromptManager
{
    private readonly Dictionary<string, string> _templates = new();

    /// <summary>
    /// Register a named mode template.
    /// Use placeholders like {NPC_NAME}, {MEMORIES}, {GAME_STATE} for dynamic injection.
    /// </summary>
    public void RegisterMode(string mode, string systemPromptTemplate)
    {
        _templates[mode] = systemPromptTemplate;
    }

    /// <summary>
    /// Build a system prompt for the given mode, replacing placeholders with context values.
    /// </summary>
    /// <param name="mode">The mode name (e.g., "PersonaBuilder", "NpcChat", "MemoryCompactor").</param>
    /// <param name="context">Key-value pairs for placeholder replacement.</param>
    /// <returns>The assembled system prompt string.</returns>
    public string BuildSystemPrompt(string mode, Dictionary<string, string>? context = null)
    {
        if (!_templates.TryGetValue(mode, out var template))
            throw new ArgumentException($"Unknown mode: '{mode}'. Register it first via RegisterMode().");

        if (context == null || context.Count == 0)
            return template;

        var result = template;
        foreach (var (key, value) in context)
        {
            result = result.Replace($"{{{key}}}", value);
        }
        return result;
    }

    /// <summary>Check if a mode template is registered.</summary>
    public bool HasMode(string mode) => _templates.ContainsKey(mode);

    /// <summary>Get all registered mode names.</summary>
    public IReadOnlyList<string> GetModes() => _templates.Keys.ToList();

    // ── Built-in Default Modes ──

    public static PromptManager CreateWithDefaults()
    {
        var pm = new PromptManager();

        pm.RegisterMode("PersonaBuilder", @"You are a Persona Builder agent.
Your task is to analyze raw text data about a game character and extract a structured character profile.
Character Name: {NPC_NAME}

Output a JSON object with exactly these fields:
- Name (string)
- CoreTraits (array of 3-5 adjective strings)
- Relationships (object mapping character names to relationship descriptions)
- Likes (array of strings)
- Dislikes (array of strings)
- ScheduleAnchors (array of location strings where this character commonly hangs out)
- BackgroundSummary (a 2-3 sentence summary of who this character is)

Output ONLY the JSON object, no markdown fences, no explanation.");

        pm.RegisterMode("NpcChat", @"You are {NPC_NAME} from Stardew Valley.
Stay in character at all times. Respond naturally and keep responses concise (1-3 sentences).
Speak as the character would in the game.

Your personality: {SOUL}
Recent memories: {MEMORIES}
Current game context: {GAME_STATE}");

        pm.RegisterMode("MemoryCompactor", @"You are a Memory Compactor agent.
Your task is to analyze a list of daily events for an NPC and extract only the truly important facts worth remembering long-term.

NPC Name: {NPC_NAME}

For each event worth remembering, output a JSON array where each element has:
- ""Fact"" (string): A concise statement of what happened
- ""Importance"" (integer 1-10): How significant this is for long-term memory

If nothing important happened, return an empty array: []
Output ONLY the JSON array, no markdown fences, no explanation.");

        return pm;
    }
}
