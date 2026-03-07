using Newtonsoft.Json;
using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// Tracks cumulative game state per NPC across days.
/// Owns both the per-day counters and the cross-day persistent state used by
/// later watchdog / prompt systems.
/// </summary>
public class GameStateTracker
{
    private readonly IMonitor? _monitor;
    private readonly string? _storagePath;
    private readonly Dictionary<string, NpcDailyState> _dailyStates = new();
    private readonly TrackerStateSnapshot _snapshot;

    public GameStateTracker(IMonitor? monitor = null, string? storagePath = null)
    {
        _monitor = monitor;
        _storagePath = storagePath;
        _snapshot = LoadSnapshot(storagePath);
    }

    /// <summary>Record a gift event for an NPC.</summary>
    public void RecordGift(string npcName, string itemName, GiftTaste taste)
    {
        var daily = GetOrCreateDaily(npcName);
        var persistent = GetOrCreatePersistent(npcName);

        daily.GiftsToday++;
        daily.LastGiftItem = itemName;
        daily.LastGiftTaste = taste;
        daily.HadInteractionToday = true;

        persistent.TotalGifts++;
        persistent.SocialFatigue += 5;
        persistent.IsCreepedOut = persistent.SocialFatigue > 15;
        persistent.LastInteractionDayKey = daily.CurrentDayKey;

        SaveSnapshot();
        Log($"[StateTracker] {npcName} received gift: {itemName} (taste={taste})", LogLevel.Debug);
    }

    /// <summary>Record a dialogue interaction.</summary>
    public void RecordDialogue(string npcName, string playerMessage)
    {
        var daily = GetOrCreateDaily(npcName);
        var persistent = GetOrCreatePersistent(npcName);

        daily.DialoguesToday++;
        daily.HadInteractionToday = true;
        daily.RecentTopics.Add(playerMessage.Length > 50 ? playerMessage[..50] : playerMessage);
        if (daily.RecentTopics.Count > 5)
            daily.RecentTopics.RemoveAt(0);

        persistent.TotalDialogues++;
        persistent.SocialFatigue += 1;
        persistent.IsCreepedOut = persistent.SocialFatigue > 15;
        persistent.LastInteractionDayKey = daily.CurrentDayKey;

        SaveSnapshot();
    }

    /// <summary>Record shipped crops from the day-ending shipping bin.</summary>
    public void RecordShipping(IEnumerable<ShippingRecord> shipments, int dayId)
    {
        var shipmentList = shipments.ToList();
        foreach (var shipment in shipmentList.Where(s => !string.IsNullOrWhiteSpace(s.ItemName) && s.Quantity > 0))
        {
            if (!_snapshot.Economy.Crops.TryGetValue(shipment.ItemName, out var stat))
            {
                stat = new CropShipmentStat();
                _snapshot.Economy.Crops[shipment.ItemName] = stat;
            }

            stat.ConsecutiveDays = stat.LastShippedDayId == dayId - 1 ? stat.ConsecutiveDays + 1 : 1;
            stat.TotalDumped += shipment.Quantity;
            stat.LastShippedDayId = dayId;
        }

        SaveSnapshot();
        Log($"[StateTracker] Recorded {shipmentList.Count} shipment entries for day {dayId}.", LogLevel.Debug);
    }

    /// <summary>Finalize the current day: decay fatigue only if there was no interaction.</summary>
    public void EndDay()
    {
        foreach (var (npcName, persistent) in _snapshot.NpcStates)
        {
            var daily = GetOrCreateDaily(npcName);
            if (!daily.HadInteractionToday)
            {
                persistent.SocialFatigue = Math.Max(0, persistent.SocialFatigue - 3);
                persistent.IsCreepedOut = persistent.SocialFatigue > 15;
            }
        }

        SaveSnapshot();
        Log("[StateTracker] Day finalized.", LogLevel.Debug);
    }

    /// <summary>Reset all daily counters. Persistent state remains.</summary>
    public void ResetDaily(string? dayKey = null)
    {
        foreach (var state in _dailyStates.Values)
        {
            state.GiftsToday = 0;
            state.LastGiftItem = null;
            state.LastGiftTaste = default;
            state.DialoguesToday = 0;
            state.RecentTopics.Clear();
            state.HadInteractionToday = false;
            state.CurrentDayKey = dayKey ?? state.CurrentDayKey;
        }

        Log("[StateTracker] Daily states reset.", LogLevel.Debug);
    }

    public NpcDailyState GetDailyState(string npcName) => GetOrCreateDaily(npcName);

    public NpcPersistentState GetPersistentState(string npcName) => GetOrCreatePersistent(npcName);

    public EconomyTrackerState GetEconomyState() => _snapshot.Economy;

    public TrackerStateSnapshot ExportSnapshot() => JsonConvert.DeserializeObject<TrackerStateSnapshot>(
        JsonConvert.SerializeObject(_snapshot)) ?? new TrackerStateSnapshot();

    /// <summary>Get a summary string for prompt injection.</summary>
    public string GetStateForPrompt(string npcName)
    {
        var daily = GetOrCreateDaily(npcName);
        var persistent = GetOrCreatePersistent(npcName);
        var parts = new List<string>();

        if (daily.GiftsToday > 0)
            parts.Add($"Player gave {daily.GiftsToday} gift(s) today (last: {daily.LastGiftItem}, reaction: {daily.LastGiftTaste}).");
        if (daily.DialoguesToday > 0)
            parts.Add($"Player has talked to me {daily.DialoguesToday} time(s) today.");
        if (daily.RecentTopics.Count > 0)
            parts.Add($"Recent conversation topics: {string.Join(", ", daily.RecentTopics)}.");

        parts.Add($"Social fatigue: {persistent.SocialFatigue}.");
        if (persistent.IsCreepedOut)
            parts.Add("I currently feel creeped out by the player's attention.");

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private NpcDailyState GetOrCreateDaily(string npcName)
    {
        if (!_dailyStates.TryGetValue(npcName, out var state))
        {
            state = new NpcDailyState();
            _dailyStates[npcName] = state;
        }

        return state;
    }

    private NpcPersistentState GetOrCreatePersistent(string npcName)
    {
        if (!_snapshot.NpcStates.TryGetValue(npcName, out var state))
        {
            state = new NpcPersistentState();
            _snapshot.NpcStates[npcName] = state;
        }

        return state;
    }

    private TrackerStateSnapshot LoadSnapshot(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || !File.Exists(storagePath))
            return new TrackerStateSnapshot();

        try
        {
            var json = File.ReadAllText(storagePath);
            return JsonConvert.DeserializeObject<TrackerStateSnapshot>(json) ?? new TrackerStateSnapshot();
        }
        catch (Exception ex)
        {
            Log($"[StateTracker] Failed to load snapshot: {ex.Message}", LogLevel.Warn);
            return new TrackerStateSnapshot();
        }
    }

    private void SaveSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_storagePath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_storagePath, JsonConvert.SerializeObject(_snapshot, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Log($"[StateTracker] Failed to save snapshot: {ex.Message}", LogLevel.Warn);
        }
    }

    private void Log(string message, LogLevel level)
    {
        _monitor?.Log(message, level);
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

public class NpcDailyState
{
    public int GiftsToday { get; set; }
    public string? LastGiftItem { get; set; }
    public GiftTaste LastGiftTaste { get; set; }
    public int DialoguesToday { get; set; }
    public List<string> RecentTopics { get; set; } = new();
    public bool HadInteractionToday { get; set; }
    public string CurrentDayKey { get; set; } = "Day0";
}
