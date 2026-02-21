using System.Text.RegularExpressions;
using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// Regex-based short-circuit cache for high-frequency trivial player inputs.
/// When the watchdog says "ShortCircuit", this provides a zero-latency response
/// without invoking any LLM call.
///
/// Responses are randomized per-NPC to avoid feeling robotic.
/// This is a pure L1 component — synchronous, O(n) where n = number of patterns.
/// </summary>
public class LexicalCache
{
    private readonly IMonitor _monitor;
    private readonly List<CacheEntry> _entries = new();
    private readonly Random _rng = new();

    public LexicalCache(IMonitor monitor)
    {
        _monitor = monitor;
        LoadDefaults();
    }

    /// <summary>
    /// Try to match the player's message against cached patterns.
    /// Returns null if no match (meaning it should fall through to LLM).
    /// </summary>
    public string? TryMatch(string npcName, string playerMessage)
    {
        var normalized = playerMessage.Trim().ToLowerInvariant();

        foreach (var entry in _entries)
        {
            if (entry.Pattern.IsMatch(normalized))
            {
                var response = entry.Responses[_rng.Next(entry.Responses.Length)];
                var result = response.Replace("{NPC}", npcName);
                _monitor.Log($"[LexicalCache] HIT: \"{playerMessage}\" → pattern={entry.Name}", LogLevel.Debug);
                return result;
            }
        }

        return null;
    }

    /// <summary>Add a custom cache entry.</summary>
    public void AddEntry(string name, string regexPattern, params string[] responses)
    {
        _entries.Add(new CacheEntry
        {
            Name = name,
            Pattern = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
            Responses = responses
        });
    }

    private void LoadDefaults()
    {
        AddEntry("Greeting", @"^(hi|hello|hey|yo|sup|嗨|你好|哈喽)\s*[!.?]*$",
            "Hey.",
            "Oh, hi there.",
            "What's up?",
            "Hey, didn't see you there.",
            "*nods*"
        );

        AddEntry("HowAreYou", @"^(how are you|how('?s| is) it going|你怎么样|你好吗|最近怎么样)\s*[?!.]*$",
            "Same as always, I guess.",
            "Can't complain... well, I could, but what's the point?",
            "Still here. That counts for something, right?",
            "Doing alright. You?"
        );

        AddEntry("Bye", @"^(bye|goodbye|see you|later|再见|拜拜|回见)\s*[!.]*$",
            "See you around.",
            "Later.",
            "Bye.",
            "Take care."
        );

        AddEntry("Thanks", @"^(thanks|thank you|thx|谢谢|感谢)\s*[!.]*$",
            "No problem.",
            "Sure thing.",
            "Don't mention it.",
            "Anytime."
        );

        AddEntry("Weather", @"^.*(天气|weather|rain|sunny|雨|晴|下雨).{0,10}$",
            "Yeah, typical Pelican Town weather.",
            "I actually don't mind the rain. It's... peaceful.",
            "Beautiful day, if you look past the clouds."
        );
    }

    private class CacheEntry
    {
        public string Name { get; set; } = "";
        public Regex Pattern { get; set; } = null!;
        public string[] Responses { get; set; } = Array.Empty<string>();
    }
}
