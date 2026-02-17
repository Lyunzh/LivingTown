using System.Collections.Concurrent;
using System.Threading.Channels;
using LivingTown.Pipeline;
using StardewModdingAPI;

namespace LivingTown.Npc;

/// <summary>
/// NPC Agent: manages NPC clients per session.
/// Consumes messages from pipeline, delegates to INpcClient (async streaming),
/// and writes resulting actions back to the pipeline.
/// </summary>
public class Agent : IAgent
{
    private readonly IMonitor _monitor;
    private readonly ConcurrentDictionary<Guid, INpcClient> _clients = new();

    // Factory for creating NPC clients (allows different NPC types)
    private readonly Func<Session, INpcClient>? _clientFactory;

    public Agent(IMonitor monitor, Func<Session, INpcClient>? clientFactory = null)
    {
        _monitor = monitor;
        _clientFactory = clientFactory;
    }

    public Endpoint SessionComes(Session session)
    {
        var outChannel = Channel.CreateUnbounded<object>();
        var inChannel = Channel.CreateUnbounded<object>();

        // Create a client for this NPC
        var client = _clientFactory?.Invoke(session)
                     ?? new SimpleNpcClient(session.NpcName);
        _clients[session.Id] = client;

        // Background loop: read events, delegate to client, write actions
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in inChannel.Reader.ReadAllAsync(session.Token))
                {
                    try
                    {
                        // Client returns async enumerable (supports streaming)
                        await foreach (var action in client.OnMessageAsync(msg, session.Token))
                        {
                            await outChannel.Writer.WriteAsync(action, session.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _monitor.Log(
                            $"[NpcAgent] Error in client {session.NpcName}: {ex.Message}",
                            LogLevel.Error);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _monitor.Log($"[NpcAgent] Session loop error: {ex}", LogLevel.Error);
            }
            finally
            {
                outChannel.Writer.TryComplete();
            }
        });

        return new Endpoint(outChannel.Reader, inChannel.Writer);
    }

    public void SessionGone(Session session)
    {
        _clients.TryRemove(session.Id, out _);
    }
}