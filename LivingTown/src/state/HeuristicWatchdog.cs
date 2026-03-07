using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// The gatekeeper: decides whether to invoke the LLM or use a cheap heuristic response.
/// Maintains a per-NPC daily entropy pool and gives the caller enough signal to
/// cleanly short-circuit low-value interactions.
/// </summary>
public class HeuristicWatchdog
{
    private readonly IMonitor? _monitor;
    private readonly Dictionary<string, float> _entropyPool = new();

    public float EscalationThreshold { get; set; } = 30f;

    private static readonly Dictionary<string, float> EventWeights = new()
    {
        ["Gift_Love"] = 15f,
        ["Gift_Hate"] = 20f,
        ["Gift_Like"] = 8f,
        ["Gift_Dislike"] = 12f,
        ["Gift_Neutral"] = 3f,
        ["Dialogue_First"] = 6f,
        ["Dialogue_Repeat"] = 2f,
        ["Dialogue_Complex"] = 15f,
        ["Dialogue_LongGap"] = 8f,
        ["MajorEvent"] = 20f,
        ["LocationChange"] = 1f,
        ["TimeChange"] = 0.5f,
        ["Festival"] = 5f,
    };

    public HeuristicWatchdog(IMonitor? monitor = null)
    {
        _monitor = monitor;
    }

    public EscalationVerdict Evaluate(string npcName, string eventType)
    {
        var weight = EventWeights.GetValueOrDefault(eventType, 1f);
        var currentEntropy = _entropyPool.GetValueOrDefault(npcName, 0f);
        var newEntropy = currentEntropy + weight;
        _entropyPool[npcName] = newEntropy;

        Log($"[Watchdog] {npcName}: +{weight} ({eventType}) → entropy={newEntropy:F1}/{EscalationThreshold}", LogLevel.Debug);

        if (newEntropy >= EscalationThreshold)
        {
            Log($"[Watchdog] ESCALATION for {npcName}: {newEntropy:F1} >= {EscalationThreshold}", LogLevel.Info);
            return EscalationVerdict.Escalate;
        }

        return EscalationVerdict.ShortCircuit;
    }

    public string ClassifyDialogue(string npcName, string playerMessage)
    {
        if (!_entropyPool.ContainsKey(npcName))
            return "Dialogue_First";
        if (playerMessage.Length > 30)
            return "Dialogue_Complex";
        return "Dialogue_Repeat";
    }

    public bool ShouldEscalateDialogue(string npcName, string playerMessage)
    {
        var eventType = ClassifyDialogue(npcName, playerMessage);
        return Evaluate(npcName, eventType) == EscalationVerdict.Escalate;
    }

    public static string ClassifyGift(GiftTaste taste) => taste switch
    {
        GiftTaste.Love => "Gift_Love",
        GiftTaste.Hate => "Gift_Hate",
        GiftTaste.Like => "Gift_Like",
        GiftTaste.Dislike => "Gift_Dislike",
        _ => "Gift_Neutral"
    };

    public float GetEntropy(string npcName) => _entropyPool.GetValueOrDefault(npcName, 0f);

    public void ResetEntropy(string npcName)
    {
        _entropyPool[npcName] = 0f;
    }

    public void ResetAllEntropy()
    {
        _entropyPool.Clear();
        Log("[Watchdog] All entropy pools reset.", LogLevel.Debug);
    }

    private void Log(string message, LogLevel level)
    {
        _monitor?.Log(message, level);
    }
}

public enum EscalationVerdict
{
    ShortCircuit,
    Escalate
}
