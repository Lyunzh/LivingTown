using System.Collections.Concurrent;
using System.Threading.Channels;
using LivingTown.Pipeline;
using StardewModdingAPI;

namespace LivingTown.Game;

/// <summary>
/// Game Agent: bridges SMAPI events and the pipeline.
/// Implements IAgent. Mirrors Go's call.Agent / freeswitch.Agent pattern.
///
/// - Out channel: game events (PlayerChat, TimeChange, etc.) → Pipeline
/// - In channel:  NPC actions (Speak, Move, Emote) ← Pipeline (executed on main thread)
/// </summary>
public class Agent : IAgent
{
    private readonly IMonitor _monitor;

    // Per-session channels
    private readonly ConcurrentDictionary<Guid, Channel<object>> _outChannels = new();
    private readonly ConcurrentDictionary<Guid, Channel<object>> _inChannels = new();

    // Pending actions to execute on main thread (polled by ModEntry.OnUpdateTicked)
    private readonly ConcurrentQueue<object> _pendingActions = new();

    public Agent(IMonitor monitor)
    {
        _monitor = monitor;
    }

    // --- IAgent implementation ---

    public Endpoint SessionComes(Session session)
    {
        var outChannel = Channel.CreateUnbounded<object>();
        var inChannel = Channel.CreateUnbounded<object>();

        _outChannels[session.Id] = outChannel;
        _inChannels[session.Id] = inChannel;
        _monitor.Log($"[GameAgent] SessionComes: {session.NpcName} (ID: {session.Id})", LogLevel.Info);

        // Start a background loop to consume actions from the Pipeline
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in inChannel.Reader.ReadAllAsync(session.Token))
                {
                    _monitor.Log($"[GameAgent] Queuing action for main thread: {msg.GetType().Name}", LogLevel.Debug);
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
    }

    // --- Main Thread API (called from ModEntry) ---

    /// <summary>
    /// Push a game event into a session's Out channel. Called from SMAPI event handlers.
    /// Non-blocking, like Go's `ch <- msg`.
    /// </summary>
    public void PublishEvent(Guid sessionId, object gameMsg)
    {
        if (_outChannels.TryGetValue(sessionId, out var ch))
        {
            if (ch.Writer.TryWrite(gameMsg))
                _monitor.Log($"[GameAgent] Published {gameMsg.GetType().Name} to session {sessionId}", LogLevel.Debug);
            else
                _monitor.Log("[GameAgent] FAILED to write event to channel!", LogLevel.Warn);
        }
        else
        {
            _monitor.Log($"[GameAgent] No channel found for session {sessionId}!", LogLevel.Warn);
        }
    }

    /// <summary>
    /// Broadcast a game event to ALL active sessions.
    /// Useful for global events like TimeChange.
    /// </summary>
    public void BroadcastEvent(object gameMsg)
    {
        foreach (var kvp in _outChannels)
        {
            kvp.Value.Writer.TryWrite(gameMsg);
        }
    }

    /// <summary>
    /// Poll pending actions to execute on the main thread.
    /// Called every game tick from ModEntry.OnUpdateTicked.
    /// </summary>
    public IEnumerable<object> PollActions()
    {
        var actions = new List<object>();
        while (_pendingActions.TryDequeue(out var action))
        {
            actions.Add(action);
        }
        return actions;
    }
}