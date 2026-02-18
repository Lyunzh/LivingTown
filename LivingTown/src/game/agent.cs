using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using LivingTown.Pipeline;

namespace LivingTown.Game;

/// <summary>
/// Game Agent: the "Body" of the mod.
/// Owns all SMAPI event subscriptions, session lifecycle, and game-side display.
/// ModEntry is just a thin entry point that wires this up.
///
/// Sensor side  (SMAPI events → Out channel → Pipeline):
///   - ButtonPressed (C key near NPC) → opens ChatInputMenu → PlayerChat
///
/// Actuator side (Pipeline → In channel → main thread queue):
///   - StreamText → progressive chatBox display
///   - Speak      → NPC speech bubble + chatBox
///   - Emote      → NPC emote
///   - Move       → (future) NPC pathfinding
/// </summary>
public class Agent : IAgent
{
    private readonly IMonitor _monitor;
    private readonly IModHelper _helper;

    // Per-session channels
    private readonly ConcurrentDictionary<Guid, Channel<object>> _outChannels = new();
    private readonly ConcurrentDictionary<Guid, Channel<object>> _inChannels = new();

    // Session lookup: NPC name → session
    private readonly Dictionary<string, Session> _npcSessions = new();

    // Streaming state: NPC name → accumulated partial text
    private readonly ConcurrentDictionary<string, string> _streamingText = new();

    // Pending actions to execute on main thread (polled by OnUpdateTicked)
    private readonly ConcurrentQueue<object> _pendingActions = new();

    // Pipeline reference — set after construction
    private Pipeline.Pipeline? _pipeline;

    public Agent(IMonitor monitor, IModHelper helper)
    {
        _monitor = monitor;
        _helper = helper;
    }

    /// <summary>
    /// Called by ModEntry after pipeline is created.
    /// </summary>
    public void SetPipeline(Pipeline.Pipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>
    /// Register SMAPI event handlers. Called from ModEntry.Entry().
    /// </summary>
    public void RegisterEvents()
    {
        _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        _helper.Events.Input.ButtonPressed += OnButtonPressed;
        _monitor.Log("[GameAgent] Events registered. Press C near an NPC to chat.", LogLevel.Info);
    }

    // =========================================================================
    // IAgent implementation
    // =========================================================================

    public Endpoint SessionComes(Session session)
    {
        var outChannel = Channel.CreateUnbounded<object>();
        var inChannel = Channel.CreateUnbounded<object>();

        _outChannels[session.Id] = outChannel;
        _inChannels[session.Id] = inChannel;
        _monitor.Log($"[GameAgent] SessionComes: {session.NpcName} (ID: {session.Id})", LogLevel.Info);

        // Background loop: drain In channel into main-thread queue
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in inChannel.Reader.ReadAllAsync(session.Token))
                {
                    _monitor.Log($"[GameAgent] Queuing action: {msg.GetType().Name}", LogLevel.Debug);
                    _pendingActions.Enqueue(msg);
                }
            }
            catch (OperationCanceledException) { }
        });

        return new Endpoint(outChannel.Reader, inChannel.Writer);
    }

    public void SessionGone(Session session)
    {
        if (_outChannels.TryRemove(session.Id, out var outCh))
            outCh.Writer.TryComplete();
        if (_inChannels.TryRemove(session.Id, out var inCh))
            inCh.Writer.TryComplete();
        _monitor.Log($"[GameAgent] SessionGone: {session.NpcName}", LogLevel.Info);
    }

    // =========================================================================
    // Sensor: SMAPI → Pipeline
    // =========================================================================

    /// <summary>
    /// C key near NPC → open ChatInputMenu.
    /// </summary>
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree) return;
        if (e.Button != SButton.C) return;

        _monitor.Log("[GameAgent] C key pressed — looking for nearby NPC...", LogLevel.Debug);

        var cursorTile = e.Cursor.GrabTile;
        var clickedNpc = Game1.currentLocation?.isCharacterAtTile(cursorTile);

        if (clickedNpc == null)
        {
            _monitor.Log($"[GameAgent] No NPC at tile ({cursorTile.X}, {cursorTile.Y})", LogLevel.Debug);
            return;
        }

        _monitor.Log($"[GameAgent] Found NPC: {clickedNpc.Name} — opening chat input.", LogLevel.Info);
        Game1.activeClickableMenu = new ChatInputMenu(clickedNpc.Name, OnPlayerChatSubmit);
    }

    /// <summary>
    /// Called when player submits text in ChatInputMenu.
    /// Creates session if needed, publishes PlayerChat to pipeline.
    /// </summary>
    private void OnPlayerChatSubmit(string npcName, string message)
    {
        _monitor.Log($"[GameAgent] Player → {npcName}: \"{message}\"", LogLevel.Info);

        // Show player message in chatBox (light blue)
        Game1.chatBox?.addMessage($"You: {message}", new Color(150, 220, 255));

        // Ensure session exists
        if (!_npcSessions.ContainsKey(npcName))
        {
            var session = new Session(npcName);
            _npcSessions[npcName] = session;
            _pipeline?.Serve(session);
            _monitor.Log($"[GameAgent] NEW session for {npcName} (ID: {session.Id})", LogLevel.Info);
        }

        // Publish PlayerChat → pipeline
        var sessionId = _npcSessions[npcName].Id;
        PublishEvent(sessionId, new GameMsg.PlayerChat(npcName, "Player", message));
    }

    /// <summary>
    /// Push a game event into a session's Out channel (non-blocking).
    /// </summary>
    public void PublishEvent(Guid sessionId, object gameMsg)
    {
        if (_outChannels.TryGetValue(sessionId, out var ch))
        {
            if (ch.Writer.TryWrite(gameMsg))
                _monitor.Log($"[GameAgent] Published {gameMsg.GetType().Name} → session {sessionId}", LogLevel.Debug);
            else
                _monitor.Log("[GameAgent] FAILED to write to channel!", LogLevel.Warn);
        }
        else
        {
            _monitor.Log($"[GameAgent] No channel for session {sessionId}!", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Broadcast a game event to ALL active sessions (e.g. TimeChange).
    /// </summary>
    public void BroadcastEvent(object gameMsg)
    {
        foreach (var kvp in _outChannels)
            kvp.Value.Writer.TryWrite(gameMsg);
    }

    // =========================================================================
    // Actuator: Pipeline → main thread
    // =========================================================================

    /// <summary>
    /// Called every game tick. Drains pending actions and executes them on the main thread.
    /// </summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady) return;

        while (_pendingActions.TryDequeue(out var action))
        {
            _monitor.Log($"[GameAgent] Executing: {action.GetType().Name}", LogLevel.Trace);
            switch (action)
            {
                case NpcMsg.StreamText stream:
                    HandleStreamText(stream);
                    break;
                case NpcMsg.Speak speak:
                    ExecuteSpeak(speak.NpcName, speak.Text);
                    break;
                case NpcMsg.Emote emote:
                    Game1.getCharacterFromName(emote.NpcName)?.doEmote(emote.EmoteId);
                    break;
                case NpcMsg.Move move:
                    _monitor.Log($"[GameAgent] Move {move.NpcName} → {move.Location} (not implemented)", LogLevel.Debug);
                    break;
            }
        }
    }

    private void HandleStreamText(NpcMsg.StreamText stream)
    {
        if (stream.IsComplete)
        {
            _streamingText.TryRemove(stream.NpcName, out _);
            _monitor.Log($"[GameAgent] Stream COMPLETE for {stream.NpcName}: \"{stream.PartialText}\"", LogLevel.Info);

            var npc = Game1.getCharacterFromName(stream.NpcName);
            if (npc != null)
            {
                var bubble = stream.PartialText.Length > 60
                    ? stream.PartialText[..57] + "..."
                    : stream.PartialText;
                npc.showTextAboveHead(bubble, duration: 4000);
            }

            Game1.chatBox?.addMessage(
                $"{stream.NpcName}: {stream.PartialText}",
                new Color(220, 220, 100));
        }
        else
        {
            var prevLen = _streamingText.GetValueOrDefault(stream.NpcName, "")?.Length ?? 0;
            _streamingText[stream.NpcName] = stream.PartialText;
            _monitor.Log($"[GameAgent] Stream partial {stream.NpcName}: {stream.PartialText.Length} chars", LogLevel.Trace);

            if (stream.PartialText.Length - prevLen >= 10 || stream.PartialText.Length <= 15)
            {
                Game1.chatBox?.addInfoMessage(
                    $"{stream.NpcName} is typing: {stream.PartialText}▌");
            }
        }
    }

    private void ExecuteSpeak(string npcName, string text)
    {
        _monitor.Log($"[GameAgent] Speak: {npcName} → \"{text}\"", LogLevel.Info);
        var npc = Game1.getCharacterFromName(npcName);
        if (npc == null)
        {
            _monitor.Log($"[GameAgent] WARNING: NPC '{npcName}' not found!", LogLevel.Warn);
            return;
        }
        var bubble = text.Length > 60 ? text[..57] + "..." : text;
        npc.showTextAboveHead(bubble, duration: 4000);
        Game1.chatBox?.addMessage($"{npcName}: {text}", new Color(220, 220, 100));
    }
}