using StardewModdingAPI;
using StardewValley;
using Newtonsoft.Json;

namespace LivingTown.State;

/// <summary>
/// Manages NPC conversation memory using a dual-layer approach:
///   - Short-term: In-RAM event buffer (List of recent events)
///   - Long-term: NPC.modData persistent storage (survives save/load)
///
/// Memory compaction (summarization via LLM) is triggered lazily:
///   - Only when event buffer exceeds MaxEventBuffer
///   - Or explicitly via DayEnding nightly batch
///
/// This is the L2 memory component. Reading is synchronous (main-thread safe).
/// Writing long-term and compaction are async (run on background threads).
/// </summary>
public class MemoryManager
{
    private readonly IMonitor _monitor;
    private const string ModDataPrefix = "LivingTown.Memory.";
    private const int MaxEventBuffer = 10;

    /// <summary>Per-NPC short-term event buffers (RAM only, reset on game restart).</summary>
    private readonly Dictionary<string, List<MemoryEvent>> _eventBuffers = new();

    public MemoryManager(IMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>
    /// Record a new event in the short-term buffer.
    /// If buffer exceeds capacity, the oldest event is dropped (hard truncation).
    /// </summary>
    public void RecordEvent(string npcName, string description, int importance = 5)
    {
        var buffer = GetOrCreateBuffer(npcName);
        buffer.Add(new MemoryEvent
        {
            Description = description,
            Importance = importance,
            GameDay = Game1.Date.TotalDays,
            Season = Game1.currentSeason,
            DayOfMonth = Game1.dayOfMonth
        });

        // Hard truncation: drop lowest-importance events when over capacity
        if (buffer.Count > MaxEventBuffer)
        {
            buffer.Sort((a, b) => a.Importance.CompareTo(b.Importance));
            buffer.RemoveAt(0); // Remove the least important
            _monitor.Log($"[Memory] {npcName}: buffer overflow, truncated least important event.", LogLevel.Debug);
        }

        _monitor.Log($"[Memory] {npcName}: recorded event (importance={importance}, buffer={buffer.Count}/{MaxEventBuffer})", LogLevel.Debug);
    }

    /// <summary>
    /// Get a formatted string of recent memories for prompt injection.
    /// Combines short-term buffer with any long-term modData summaries.
    /// </summary>
    public string GetMemoriesForPrompt(string npcName)
    {
        var parts = new List<string>();

        // Long-term memories from modData
        var longTerm = GetLongTermMemory(npcName);
        if (!string.IsNullOrEmpty(longTerm))
            parts.Add($"[Long-term memories]\n{longTerm}");

        // Short-term buffer
        var buffer = GetOrCreateBuffer(npcName);
        if (buffer.Count > 0)
        {
            var recentLines = buffer
                .OrderByDescending(e => e.Importance)
                .Take(5)
                .Select(e => $"- (day {e.DayOfMonth} {e.Season}, importance={e.Importance}) {e.Description}");
            parts.Add($"[Recent events today]\n{string.Join("\n", recentLines)}");
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : "No memories recorded yet.";
    }

    /// <summary>
    /// Save a compacted summary to NPC's modData for long-term persistence.
    /// Usually called after LLM-based compaction during DayEnding.
    /// </summary>
    public void SaveLongTermMemory(string npcName, string summary)
    {
        var npc = Game1.getCharacterFromName(npcName);
        if (npc == null)
        {
            _monitor.Log($"[Memory] Cannot save long-term memory: NPC '{npcName}' not found.", LogLevel.Warn);
            return;
        }

        var key = $"{ModDataPrefix}{npcName}.Summary";
        npc.modData[key] = summary;
        _monitor.Log($"[Memory] Saved long-term memory for {npcName} ({summary.Length} chars).", LogLevel.Info);
    }

    /// <summary>Retrieve long-term memory summary from NPC modData.</summary>
    public string? GetLongTermMemory(string npcName)
    {
        var npc = Game1.getCharacterFromName(npcName);
        var key = $"{ModDataPrefix}{npcName}.Summary";
        return npc?.modData.TryGetValue(key, out var value) == true ? value : null;
    }

    /// <summary>Get the current event buffer count for an NPC.</summary>
    public int GetBufferCount(string npcName) =>
        _eventBuffers.TryGetValue(npcName, out var buf) ? buf.Count : 0;

    /// <summary>Check if an NPC's buffer is close to overflowing (triggers compaction hint).</summary>
    public bool ShouldCompact(string npcName) => GetBufferCount(npcName) >= MaxEventBuffer - 2;

    /// <summary>Clear the short-term buffer after compaction.</summary>
    public void ClearBuffer(string npcName)
    {
        if (_eventBuffers.ContainsKey(npcName))
            _eventBuffers[npcName].Clear();
    }

    private List<MemoryEvent> GetOrCreateBuffer(string npcName)
    {
        if (!_eventBuffers.TryGetValue(npcName, out var buffer))
        {
            buffer = new List<MemoryEvent>();
            _eventBuffers[npcName] = buffer;
        }
        return buffer;
    }
}

/// <summary>A single memory event in the short-term buffer.</summary>
public class MemoryEvent
{
    public string Description { get; set; } = "";
    public int Importance { get; set; } = 5;
    public int GameDay { get; set; }
    public string Season { get; set; } = "";
    public int DayOfMonth { get; set; }
}
