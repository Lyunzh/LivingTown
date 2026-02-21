using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// The gatekeeper: decides whether to invoke the LLM or use a cheap heuristic response.
///
/// Maintains a per-NPC daily entropy pool. Low-entropy events (casual greetings)
/// just increment the counter. When entropy exceeds the threshold, the Watchdog
/// signals that the ReACT Agent should be invoked for deep reasoning.
///
/// This is a pure L1 component — synchronous, O(1), runs on the main thread in ~0.01ms.
/// It does NOT create Agents. It merely returns a verdict: Escalate or ShortCircuit.
/// </summary>
public class HeuristicWatchdog
{
    private readonly IMonitor _monitor;

    /// <summary>Per-NPC entropy pool for the current day.</summary>
    private readonly Dictionary<string, float> _entropyPool = new();

    /// <summary>Entropy threshold above which we escalate to LLM.</summary>
    public float EscalationThreshold { get; set; } = 30f;

    // ── Event Weights (hardcoded heuristic table) ──
    private static readonly Dictionary<string, float> EventWeights = new()
    {
        ["Gift_Love"] = 15f,
        ["Gift_Hate"] = 20f,
        ["Gift_Like"] = 8f,
        ["Gift_Dislike"] = 12f,
        ["Gift_Neutral"] = 3f,
        ["Dialogue_First"] = 10f,      // First dialogue of the day
        ["Dialogue_Repeat"] = 2f,      // Subsequent dialogues
        ["Dialogue_Complex"] = 15f,    // Player message > 30 chars (likely a real question)
        ["LocationChange"] = 1f,
        ["TimeChange"] = 0.5f,
        ["Festival"] = 5f,
    };

    public HeuristicWatchdog(IMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Feed an event to the watchdog and get the verdict.
    /// This is the ONLY public method that matters.
    /// </summary>
    /// <returns>EscalationVerdict: whether the LLM should be invoked.</returns>
    public EscalationVerdict Evaluate(string npcName, string eventType)
    {
        var weight = EventWeights.GetValueOrDefault(eventType, 1f);
        var currentEntropy = _entropyPool.GetValueOrDefault(npcName, 0f);
        var newEntropy = currentEntropy + weight;
        _entropyPool[npcName] = newEntropy;

        _monitor.Log($"[Watchdog] {npcName}: +{weight} ({eventType}) → entropy={newEntropy:F1}/{EscalationThreshold}", LogLevel.Debug);

        if (newEntropy >= EscalationThreshold)
        {
            _monitor.Log($"[Watchdog] ⚡ ESCALATION for {npcName}! Entropy {newEntropy:F1} >= {EscalationThreshold}", LogLevel.Info);
            return EscalationVerdict.Escalate;
        }

        return EscalationVerdict.ShortCircuit;
    }

    /// <summary>
    /// Classify a player message to determine which event type it triggers.
    /// </summary>
    public string ClassifyDialogue(string npcName, string playerMessage)
    {
        var state = _entropyPool.GetValueOrDefault(npcName, 0f);
        if (state < 1f)
            return "Dialogue_First";
        if (playerMessage.Length > 30)
            return "Dialogue_Complex";
        return "Dialogue_Repeat";
    }

    /// <summary>
    /// Classify a gift event based on the NPC's reaction.
    /// </summary>
    public static string ClassifyGift(GiftTaste taste) => taste switch
    {
        GiftTaste.Love => "Gift_Love",
        GiftTaste.Hate => "Gift_Hate",
        GiftTaste.Like => "Gift_Like",
        GiftTaste.Dislike => "Gift_Dislike",
        _ => "Gift_Neutral"
    };

    /// <summary>Get current entropy for an NPC.</summary>
    public float GetEntropy(string npcName) => _entropyPool.GetValueOrDefault(npcName, 0f);

    /// <summary>Reset a specific NPC's entropy pool.</summary>
    public void ResetEntropy(string npcName)
    {
        _entropyPool[npcName] = 0f;
    }

    /// <summary>Reset all entropy pools. Call on DayStarted.</summary>
    public void ResetAllEntropy()
    {
        _entropyPool.Clear();
        _monitor.Log("[Watchdog] All entropy pools reset.", LogLevel.Debug);
    }
}

/// <summary>The watchdog's verdict: should we invoke the LLM?</summary>
public enum EscalationVerdict
{
    /// <summary>Entropy is low. Use LexicalCache or template response. Do NOT invoke LLM.</summary>
    ShortCircuit,

    /// <summary>Entropy is high. Invoke the full ReACT Agent with LLM.</summary>
    Escalate
}
