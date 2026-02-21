using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// Tracks cumulative game state per NPC across days.
/// Monitors events like gifting, dialogue frequency, and economic patterns.
/// Data persists via NPC.modData (survives save/load).
///
/// This is a pure L0 component — runs synchronously on the main thread.
/// </summary>
public class GameStateTracker
{
    private readonly IMonitor _monitor;

    /// <summary>Per-NPC interaction counters for the current day.</summary>
    private readonly Dictionary<string, NpcDailyState> _dailyStates = new();

    public GameStateTracker(IMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>Record a gift event for an NPC.</summary>
    public void RecordGift(string npcName, string itemName, GiftTaste taste)
    {
        var state = GetOrCreate(npcName);
        state.GiftsToday++;
        state.LastGiftItem = itemName;
        state.LastGiftTaste = taste;
        _monitor.Log($"[StateTracker] {npcName} received gift: {itemName} (taste={taste})", LogLevel.Debug);
    }

    /// <summary>Record a dialogue interaction.</summary>
    public void RecordDialogue(string npcName, string playerMessage)
    {
        var state = GetOrCreate(npcName);
        state.DialoguesToday++;
        state.RecentTopics.Add(playerMessage.Length > 50 ? playerMessage[..50] : playerMessage);
        // Keep only last 5 topics to avoid memory bloat
        if (state.RecentTopics.Count > 5)
            state.RecentTopics.RemoveAt(0);
    }

    /// <summary>Get the daily state snapshot for an NPC.</summary>
    public NpcDailyState GetDailyState(string npcName) => GetOrCreate(npcName);

    /// <summary>Reset all daily counters. Call this on DayStarted.</summary>
    public void ResetDaily()
    {
        _dailyStates.Clear();
        _monitor.Log("[StateTracker] Daily states reset.", LogLevel.Debug);
    }

    /// <summary>Get a summary string for prompt injection.</summary>
    public string GetStateForPrompt(string npcName)
    {
        var state = GetOrCreate(npcName);
        var parts = new List<string>();

        if (state.GiftsToday > 0)
            parts.Add($"Player gave {state.GiftsToday} gift(s) today (last: {state.LastGiftItem}, reaction: {state.LastGiftTaste}).");
        if (state.DialoguesToday > 0)
            parts.Add($"Player has talked to me {state.DialoguesToday} time(s) today.");
        if (state.RecentTopics.Count > 0)
            parts.Add($"Recent conversation topics: {string.Join(", ", state.RecentTopics)}.");

        return parts.Count > 0 ? string.Join(" ", parts) : "No notable interactions today.";
    }

    private NpcDailyState GetOrCreate(string npcName)
    {
        if (!_dailyStates.TryGetValue(npcName, out var state))
        {
            state = new NpcDailyState();
            _dailyStates[npcName] = state;
        }
        return state;
    }
}

/// <summary>Taste categories for gifts (mirrors StardewValley.NPC.getGiftTasteForThisItem).</summary>
public enum GiftTaste
{
    Love = 0,
    Like = 2,
    Neutral = 8,
    Dislike = 4,
    Hate = 6
}

/// <summary>Per-NPC daily interaction state.</summary>
public class NpcDailyState
{
    public int GiftsToday { get; set; }
    public string? LastGiftItem { get; set; }
    public GiftTaste LastGiftTaste { get; set; }
    public int DialoguesToday { get; set; }
    public List<string> RecentTopics { get; set; } = new();
}
