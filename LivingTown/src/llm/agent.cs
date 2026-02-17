using System.Collections.Concurrent;
using System.Threading.Channels;
using LivingTown.Pipeline;
using StardewModdingAPI;

namespace LivingTown.LLM;

/// <summary>
/// LLM Agent: manages LLM clients per session.
/// When receiving a RequestLLM, creates a streaming response and emits
/// a single StreamingResponse message carrying the live IAsyncEnumerable.
/// </summary>
public class Agent : IAgent
{
    private readonly IMonitor _monitor;
    private readonly ConcurrentDictionary<Guid, ILLMClient> _clients = new();

    public Agent(IMonitor monitor)
    {
        _monitor = monitor;
    }

    public Endpoint SessionComes(Session session)
    {
        var outChannel = Channel.CreateUnbounded<object>();
        var inChannel = Channel.CreateUnbounded<object>();

        // Create client for this session
        var client = new LLMClient(session, _monitor);
        _clients[session.Id] = client;

        // Background loop: read requests, create streaming responses
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in inChannel.Reader.ReadAllAsync(session.Token))
                {
                    switch (msg)
                    {
                        case NpcMsg.RequestLLM request:
                            // Create the stream from the LLM client
                            var tokenStream = client.GenerateStreamingResponseAsync(
                                request.Prompt, session.Token);

                            // Emit ONE message carrying the live stream object
                            await outChannel.Writer.WriteAsync(
                                new LLMMsg.StreamingResponse(
                                    request.NpcName,
                                    request.DialogRound,
                                    tokenStream),
                                session.Token
                            );
                            _monitor.Log(
                                $"[LLMAgent] StreamingResponse created for {request.NpcName}",
                                LogLevel.Debug);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _monitor.Log($"[LLMAgent] Session loop error: {ex}", LogLevel.Error);
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