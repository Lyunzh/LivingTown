using System.Collections.Concurrent;
using System.Threading.Channels;
using LivingTown.Pipeline;
using StardewModdingAPI;

namespace LivingTown.LLM;

/// <summary>
/// LLM Agent: manages LLM clients per session.
/// Receives the mod directory path to locate .env.
/// </summary>
public class Agent : IAgent
{
    private readonly IMonitor _monitor;
    private readonly string _modDir;
    private readonly ConcurrentDictionary<Guid, ILLMClient> _clients = new();

    public Agent(IMonitor monitor, string modDir)
    {
        _monitor = monitor;
        _modDir = modDir;
    }

    public Endpoint SessionComes(Session session)
    {
        var outChannel = Channel.CreateUnbounded<object>();
        var inChannel = Channel.CreateUnbounded<object>();

        var client = new LLMClient(session, _monitor, _modDir);
        _clients[session.Id] = client;
        _monitor.Log($"[LLMAgent] SessionComes: {session.NpcName} (ID: {session.Id})", LogLevel.Info);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in inChannel.Reader.ReadAllAsync(session.Token))
                {
                    switch (msg)
                    {
                        case NpcMsg.RequestLLM request:
                            _monitor.Log($"[LLMAgent] RequestLLM: \"{request.Prompt}\"", LogLevel.Debug);
                            var tokenStream = client.GenerateStreamingResponseAsync(request.Prompt, session.Token);
                            await outChannel.Writer.WriteAsync(
                                new LLMMsg.StreamingResponse(request.NpcName, request.DialogRound, tokenStream),
                                session.Token);
                            _monitor.Log($"[LLMAgent] StreamingResponse emitted for {request.NpcName}", LogLevel.Debug);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _monitor.Log($"[LLMAgent] Error: {ex}", LogLevel.Error);
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
        _monitor.Log($"[LLMAgent] SessionGone: {session.NpcName}", LogLevel.Info);
    }
}