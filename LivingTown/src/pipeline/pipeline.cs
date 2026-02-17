using System.Threading.Channels;
using StardewModdingAPI;

namespace LivingTown.Pipeline;

/// <summary>
/// Manages agents and runs the select-loop to route messages between them.
/// </summary>
public class Pipeline
{
    private readonly IMonitor _monitor;

    // Agents (global singletons)
    private readonly IAgent _gameAgent;
    private readonly IAgent _llmAgent;
    private readonly IAgent _npcAgent;

    // Active sessions
    private readonly Dictionary<Guid, Session> _sessions = new();
    private readonly object _sessionsLock = new();

    public Pipeline(IMonitor monitor, IAgent gameAgent, IAgent llmAgent, IAgent npcAgent)
    {
        _monitor = monitor;
        _gameAgent = gameAgent;
        _llmAgent = llmAgent;
        _npcAgent = npcAgent;
    }

    /// <summary>
    /// Start serving a session. Creates endpoints from all agents and spins up the flow loop.
    /// </summary>
    public void Serve(Session session)
    {
        lock (_sessionsLock)
        {
            _sessions[session.Id] = session;
        }

        var endpoints = PrepareEndpoints(session);

        // Fire the flow loop on a background thread
        _ = Task.Run(async () =>
        {
            try
            {
                _monitor.Log($"[Pipeline] Session {session.Id} ({session.NpcName}) flow started.", LogLevel.Info);
                await FlowAsync(session, endpoints);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                _monitor.Log($"[Pipeline] CRITICAL error in session {session.Id}: {ex}", LogLevel.Error);
            }
            finally
            {
                Terminate(session.Id);
            }
        });
    }

    /// <summary>
    /// Terminate a session. Mirrors Go's pipeline.Terminate().
    /// </summary>
    public void Terminate(Guid sessionId)
    {
        Session session;
        lock (_sessionsLock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
                return;
            _sessions.Remove(sessionId);
        }

        session.Cancel();

        _gameAgent.SessionGone(session);
        _llmAgent.SessionGone(session);
        _npcAgent.SessionGone(session);

        _monitor.Log($"[Pipeline] Session {sessionId} terminated.", LogLevel.Info);
    }

    /// <summary>
    /// Prepare endpoints from each agent.
    /// </summary>
    private AgentEndpoints PrepareEndpoints(Session session)
    {
        return new AgentEndpoints
        {
            Game = _gameAgent.SessionComes(session),
            LLM = _llmAgent.SessionComes(session),
            Npc = _npcAgent.SessionComes(session),
        };
    }

    /// <summary>
    /// The heart of the pipeline: a for-select loop.
    /// In C#, we use Task.WhenAny on ReadAsync tasks.
    /// </summary>
    private async Task FlowAsync(Session session, AgentEndpoints endpoints)
    {
        var ct = session.Token;

        while (!ct.IsCancellationRequested)
        {
            var gameTask = endpoints.Game.Out.ReadAsync(ct).AsTask();
            var llmTask = endpoints.LLM.Out.ReadAsync(ct).AsTask();
            var npcTask = endpoints.Npc.Out.ReadAsync(ct).AsTask();
            
            var completed = await Task.WhenAny(gameTask, llmTask, npcTask);

            if (completed == gameTask)
            {
                var msg = await gameTask;
                OnGameMessage(session, endpoints, msg);
            }
            else if (completed == llmTask)
            {
                var msg = await llmTask;
                OnLLMMessage(session, endpoints, msg);
            }
            else if (completed == npcTask)
            {
                var msg = await npcTask;
                OnNpcMessage(session, endpoints, msg);
            }
        }
    }

    // --- Message Handlers  ---

    private void OnGameMessage(Session session, AgentEndpoints endpoints, object data)
    {
        switch (data)
        {
            case GameMsg.PlayerChat chat:
                _monitor.Log($"[GAME>>>] Player said to {chat.NpcName}: {chat.Message}", LogLevel.Debug);
                // Route to NPC agent for decision-making
                endpoints.Npc.In.TryWrite(data);
                break;

            case GameMsg.TimeChange time:
                _monitor.Log($"[GAME>>>] Time changed to {time.Time}", LogLevel.Trace);
                // Broadcast to NPC
                endpoints.Npc.In.TryWrite(data);
                break;

            case GameMsg.LocationChange loc:
                _monitor.Log($"[GAME>>>] {loc.NpcName} moved to {loc.NewLocation}", LogLevel.Trace);
                endpoints.Npc.In.TryWrite(data);
                break;
        }
    }

    private void OnLLMMessage(Session session, AgentEndpoints endpoints, object data)
    {
        switch (data)
        {
            case LLMMsg.StreamingResponse streaming:
                _monitor.Log($"[LLM>>>] StreamingResponse for {session.NpcName}", LogLevel.Debug);
                // Route the stream message to NPC agent — it will consume the stream
                endpoints.Npc.In.TryWrite(data);
                break;
        }
    }

    private void OnNpcMessage(Session session, AgentEndpoints endpoints, object data)
    {
        switch (data)
        {
            case NpcMsg.RequestLLM request:
                _monitor.Log($"[NPC>>>] {session.NpcName} requesting LLM: {request.Prompt}", LogLevel.Debug);
                // Route to LLM agent
                endpoints.LLM.In.TryWrite(data);
                break;

            case NpcMsg.StreamText streamText:
                _monitor.Log($"[NPC>>>] {streamText.NpcName} stream: {(streamText.IsComplete ? "DONE" : "partial")} ({streamText.PartialText.Length} chars)", LogLevel.Trace);
                // Route to game agent for progressive display
                endpoints.Game.In.TryWrite(data);
                break;

            case NpcMsg.Speak speak:
                _monitor.Log($"[NPC>>>] {speak.NpcName} says: {speak.Text}", LogLevel.Debug);
                endpoints.Game.In.TryWrite(data);
                break;

            case NpcMsg.Move move:
                _monitor.Log($"[NPC>>>] {move.NpcName} moves to {move.Location}", LogLevel.Debug);
                endpoints.Game.In.TryWrite(data);
                break;

            case NpcMsg.Emote emote:
                _monitor.Log($"[NPC>>>] {emote.NpcName} emotes {emote.EmoteId}", LogLevel.Debug);
                endpoints.Game.In.TryWrite(data);
                break;
        }
    }
}