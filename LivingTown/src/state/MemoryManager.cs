using Newtonsoft.Json;
using StardewModdingAPI;

namespace LivingTown.State;

/// <summary>
/// Manages NPC memory using a pragmatic dual-layer model:
/// - short-term in-memory event buffers for today's interactions;
/// - long-term persisted summaries with nightly compaction and decay.
/// </summary>
public class MemoryManager
{
    private readonly IMonitor? _monitor;
    private readonly string? _storagePath;
    private readonly Func<MemoryContext> _contextProvider;

    private const int MaxEventBuffer = 10;
    private const int MaxPromptLongTermMemories = 8;
    private readonly Dictionary<string, List<MemoryEvent>> _eventBuffers = new();
    private readonly Dictionary<string, List<LongTermMemory>> _longTermMemories;

    public MemoryManager(IMonitor? monitor = null, string? storagePath = null, Func<MemoryContext>? contextProvider = null)
    {
        _monitor = monitor;
        _storagePath = storagePath;
        _contextProvider = contextProvider ?? (() => new MemoryContext(0, "Unknown", 0));
        _longTermMemories = LoadSnapshot(storagePath);
    }

    public void RecordEvent(string npcName, string description, int importance = 5)
    {
        var context = _contextProvider();
        var buffer = GetOrCreateBuffer(npcName);
        buffer.Add(new MemoryEvent
        {
            Description = description,
            Importance = Math.Clamp(importance, 1, 10),
            GameDay = context.GameDay,
            Season = context.Season,
            DayOfMonth = context.DayOfMonth,
            Keywords = ExtractKeywords(description)
        });

        if (buffer.Count > MaxEventBuffer)
        {
            buffer.Sort((a, b) => a.Importance.CompareTo(b.Importance));
            buffer.RemoveAt(0);
            Log($"[Memory] {npcName}: buffer overflow, truncated least important event.", LogLevel.Debug);
        }

        Log($"[Memory] {npcName}: recorded event (importance={importance}, buffer={buffer.Count}/{MaxEventBuffer})", LogLevel.Debug);
    }

    public string GetMemoriesForPrompt(string npcName, string? query = null)
    {
        var context = _contextProvider();
        var parts = new List<string>();

        var longTerm = GetRelevantLongTermMemories(npcName, context.GameDay, query, MaxPromptLongTermMemories);
        if (longTerm.Count > 0)
        {
            var lines = longTerm.Select(memory =>
                $"- (importance={memory.Importance}, score={GetScore(memory, context.GameDay):0.0}) {memory.Fact}");
            parts.Add($"[Long-term memories]\n{string.Join("\n", lines)}");
        }

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

    public IReadOnlyList<LongTermMemory> GetRelevantLongTermMemories(string npcName, int currentDay, string? query = null, int maxCount = 8)
    {
        if (!_longTermMemories.TryGetValue(npcName, out var memories))
            return Array.Empty<LongTermMemory>();

        var queryKeywords = ExtractKeywords(query ?? string.Empty);
        return memories
            .Select(memory => new { Memory = memory, Score = GetScore(memory, currentDay) + GetQueryBoost(memory, queryKeywords) })
            .Where(x => x.Score >= 2f || x.Memory.Permanent)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Memory.Importance)
            .Take(maxCount)
            .Select(x => x.Memory)
            .ToList();
    }

    public void RunNightlyMaintenance(int currentDay)
    {
        foreach (var npcName in _eventBuffers.Keys.ToList())
        {
            CompactNpcMemories(npcName);
        }

        DecayLongTermMemories(currentDay);
        SaveSnapshot();
        Log("[Memory] Nightly maintenance complete.", LogLevel.Debug);
    }

    public IReadOnlyList<LongTermMemory> GetAllLongTermMemories(string npcName)
    {
        return _longTermMemories.TryGetValue(npcName, out var memories)
            ? memories.OrderByDescending(m => m.Importance).ToList()
            : Array.Empty<LongTermMemory>();
    }

    public int GetBufferCount(string npcName) =>
        _eventBuffers.TryGetValue(npcName, out var buf) ? buf.Count : 0;

    public bool ShouldCompact(string npcName) => GetBufferCount(npcName) >= MaxEventBuffer - 2;

    public void ClearBuffer(string npcName)
    {
        if (_eventBuffers.ContainsKey(npcName))
            _eventBuffers[npcName].Clear();
    }

    private void CompactNpcMemories(string npcName)
    {
        var buffer = GetOrCreateBuffer(npcName);
        if (buffer.Count == 0)
            return;

        var target = GetOrCreateLongTerm(npcName);
        foreach (var memoryEvent in buffer
                     .OrderByDescending(e => e.Importance)
                     .GroupBy(e => e.Description, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()))
        {
            if (memoryEvent.Importance < 4)
                continue;

            var existing = target.FirstOrDefault(x => string.Equals(x.Fact, memoryEvent.Description, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Importance = Math.Max(existing.Importance, memoryEvent.Importance);
                existing.LastReferencedDay = Math.Max(existing.LastReferencedDay, memoryEvent.GameDay);
                existing.Keywords = existing.Keywords.Union(memoryEvent.Keywords, StringComparer.OrdinalIgnoreCase).ToList();
                continue;
            }

            target.Add(new LongTermMemory
            {
                Fact = memoryEvent.Description,
                Importance = memoryEvent.Importance,
                GameDay = memoryEvent.GameDay,
                DayOfMonth = memoryEvent.DayOfMonth,
                Season = memoryEvent.Season,
                Permanent = memoryEvent.Importance >= 9,
                LastReferencedDay = memoryEvent.GameDay,
                Keywords = memoryEvent.Keywords.ToList()
            });
        }

        buffer.Clear();
        Log($"[Memory] {npcName}: compacted nightly memories.", LogLevel.Debug);
    }

    private void DecayLongTermMemories(int currentDay)
    {
        foreach (var npcName in _longTermMemories.Keys.ToList())
        {
            var memories = _longTermMemories[npcName]
                .Where(memory => memory.Permanent || GetScore(memory, currentDay) >= 2f)
                .OrderByDescending(memory => GetScore(memory, currentDay))
                .ThenByDescending(memory => memory.Importance)
                .Take(20)
                .ToList();

            _longTermMemories[npcName] = memories;
        }
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

    private List<LongTermMemory> GetOrCreateLongTerm(string npcName)
    {
        if (!_longTermMemories.TryGetValue(npcName, out var memories))
        {
            memories = new List<LongTermMemory>();
            _longTermMemories[npcName] = memories;
        }
        return memories;
    }

    private Dictionary<string, List<LongTermMemory>> LoadSnapshot(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || !File.Exists(storagePath))
            return new Dictionary<string, List<LongTermMemory>>();

        try
        {
            var json = File.ReadAllText(storagePath);
            return JsonConvert.DeserializeObject<Dictionary<string, List<LongTermMemory>>>(json)
                   ?? new Dictionary<string, List<LongTermMemory>>();
        }
        catch (Exception ex)
        {
            Log($"[Memory] Failed to load snapshot: {ex.Message}", LogLevel.Warn);
            return new Dictionary<string, List<LongTermMemory>>();
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

            File.WriteAllText(_storagePath, JsonConvert.SerializeObject(_longTermMemories, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Log($"[Memory] Failed to save snapshot: {ex.Message}", LogLevel.Warn);
        }
    }

    private static float GetScore(LongTermMemory memory, int currentDay)
    {
        if (memory.Permanent)
            return memory.Importance + 100f;

        return memory.Importance - Math.Max(0, currentDay - memory.GameDay) * 0.1f;
    }

    private static float GetQueryBoost(LongTermMemory memory, IReadOnlyCollection<string> queryKeywords)
    {
        if (queryKeywords.Count == 0)
            return 0f;

        var overlap = memory.Keywords.Intersect(queryKeywords, StringComparer.OrdinalIgnoreCase).Count();
        return overlap * 2f;
    }

    private static List<string> ExtractKeywords(string text)
    {
        return text
            .Split(new[] { ' ', ',', '.', ':', ';', '"', '\'', '?', '!', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Log(string message, LogLevel level)
    {
        _monitor?.Log(message, level);
    }
}

public sealed record MemoryContext(int GameDay, string Season, int DayOfMonth);

public class MemoryEvent
{
    public string Description { get; set; } = "";
    public int Importance { get; set; } = 5;
    public int GameDay { get; set; }
    public string Season { get; set; } = "";
    public int DayOfMonth { get; set; }
    public List<string> Keywords { get; set; } = new();
}

public class LongTermMemory
{
    public string Fact { get; set; } = "";
    public int Importance { get; set; }
    public int GameDay { get; set; }
    public string Season { get; set; } = "";
    public int DayOfMonth { get; set; }
    public bool Permanent { get; set; }
    public int LastReferencedDay { get; set; }
    public List<string> Keywords { get; set; } = new();
}
