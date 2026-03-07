using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using LivingTown.LLM.Core;
using LivingTown.State;

namespace LivingTown.Game;

public class ChatCoordinator
{
    private readonly IMonitor _monitor;
    private readonly AgentFactory _agentFactory;
    private readonly SoulLoader _soulLoader;
    private readonly GameStateTracker _stateTracker;
    private readonly HeuristicWatchdog _watchdog;
    private readonly LexicalCache _lexicalCache;
    private readonly MemoryManager _memoryManager;
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private readonly ConcurrentDictionary<string, bool> _pendingLlmCalls = new();

    public ChatCoordinator(
        IMonitor monitor,
        AgentFactory agentFactory,
        SoulLoader soulLoader,
        GameStateTracker stateTracker,
        HeuristicWatchdog watchdog,
        LexicalCache lexicalCache,
        MemoryManager memoryManager)
    {
        _monitor = monitor;
        _agentFactory = agentFactory;
        _soulLoader = soulLoader;
        _stateTracker = stateTracker;
        _watchdog = watchdog;
        _lexicalCache = lexicalCache;
        _memoryManager = memoryManager;
    }

    public void OnPlayerChat(string npcName, string message)
    {
        _monitor.Log($"[Chat] Player → {npcName}: \"{message}\"", LogLevel.Info);
        Game1.chatBox?.addMessage($"You: {message}", new Color(150, 220, 255));

        _stateTracker.RecordDialogue(npcName, message);
        _memoryManager.RecordEvent(npcName, $"Player said: \"{message}\"", importance: message.Length > 30 ? 5 : 3);

        var shouldEscalate = _watchdog.ShouldEscalateDialogue(npcName, message);
        if (!shouldEscalate)
        {
            var cached = _lexicalCache.TryMatch(npcName, message);
            if (cached != null)
            {
                _monitor.Log($"[Chat] LexicalCache hit for {npcName}", LogLevel.Debug);
                DisplayNpcResponse(npcName, cached);
                return;
            }

            var fallback = BuildHeuristicFallback(npcName, message);
            DisplayNpcResponse(npcName, fallback);
            _memoryManager.RecordEvent(npcName, $"I answered heuristically: \"{Truncate(fallback, 80)}\"", importance: 2);
            return;
        }

        if (_pendingLlmCalls.TryAdd(npcName, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await InvokeLlmAsync(npcName, message);
                    _mainThreadQueue.Enqueue(() => DisplayNpcResponse(npcName, response));
                }
                catch (Exception ex)
                {
                    _monitor.Log($"[Chat] LLM error: {ex.Message}", LogLevel.Error);
                    _mainThreadQueue.Enqueue(() =>
                        DisplayNpcResponse(npcName, "...I lost my train of thought."));
                }
                finally
                {
                    _pendingLlmCalls.TryRemove(npcName, out _);
                }
            });

            Game1.chatBox?.addInfoMessage($"{npcName} is thinking...");
        }
        else
        {
            Game1.chatBox?.addInfoMessage($"{npcName} is still thinking...");
        }
    }

    public void Tick()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _monitor.Log($"[Chat] Tick action error: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private async Task<string> InvokeLlmAsync(string npcName, string playerMessage)
    {
        var soul = _soulLoader.GetSoulForPrompt(npcName);
        var memories = _memoryManager.GetMemoriesForPrompt(npcName, playerMessage);
        var gameState = _stateTracker.GetStateForPrompt(npcName);

        var context = new Dictionary<string, string>
        {
            ["NPC_NAME"] = npcName,
            ["SOUL"] = soul,
            ["MEMORIES"] = memories,
            ["GAME_STATE"] = gameState
        };

        var result = await _agentFactory.RunAsync(
            mode: "NpcChat",
            objective: playerMessage,
            context: context,
            maxIterations: 3);

        if (result.Error != null)
        {
            _monitor.Log($"[Chat] Agent error: {result.Error}", LogLevel.Warn);
            return "...something's on my mind, but I can't put it into words.";
        }

        _memoryManager.RecordEvent(npcName,
            $"I responded: \"{Truncate(result.FinalAnswer, 80)}\"",
            importance: 4);

        return result.FinalAnswer;
    }

    private string BuildHeuristicFallback(string npcName, string message)
    {
        var daily = _stateTracker.GetDailyState(npcName);
        var persistent = _stateTracker.GetPersistentState(npcName);
        var lower = message.Trim().ToLowerInvariant();

        if (persistent.IsCreepedOut || persistent.SocialFatigue >= 15)
            return "Can we slow down a little? We've talked a lot already.";

        if (daily.DialoguesToday >= 4)
            return "We've already been talking quite a bit today.";

        if (lower.Contains("why") || lower.Contains("怎么") || lower.Contains("为什么"))
            return "That's not a quick answer. Ask me again when I have a moment.";

        return "Mm. I hear you.";
    }

    private void DisplayNpcResponse(string npcName, string text)
    {
        var npc = Game1.getCharacterFromName(npcName);
        if (npc != null)
        {
            var bubble = text.Length > 60 ? text[..57] + "..." : text;
            npc.showTextAboveHead(bubble, duration: 4000);
        }
        Game1.chatBox?.addMessage($"{npcName}: {text}", new Color(220, 220, 100));
        _monitor.Log($"[Chat] {npcName}: \"{text}\"", LogLevel.Info);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
