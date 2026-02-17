using System.Threading.Channels;

namespace LivingTown.Pipeline;

/// <summary>
/// Each agent exposes a pair of channels per session for the Pipeline to multiplex.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Called when a new session begins. Returns an Endpoint (Out, In channel pair).
    /// The agent starts its internal processing loop.
    /// </summary>
    Endpoint SessionComes(Session session);

    /// <summary>
    /// Called when a session ends. Agent cleans up resources for this session.
    /// </summary>
    void SessionGone(Session session);
}

/// <summary>
/// A pair of channels connecting an Agent to the Pipeline.
/// </summary>
public class Endpoint
{
    /// <summary>Out: Agent → Pipeline (the pipeline reads from this)</summary>
    public ChannelReader<object> Out { get; }

    /// <summary>In: Pipeline → Agent (the pipeline writes to this)</summary>
    public ChannelWriter<object> In { get; }

    public Endpoint(ChannelReader<object> outReader, ChannelWriter<object> inWriter)
    {
        Out = outReader;
        In = inWriter;
    }
}

/// <summary>
/// Represents a game session (one active "conversation flow" with NPCs).
/// </summary>
public class Session
{
    public Guid Id { get; } = Guid.NewGuid();
    public string NpcName { get; }
    public CancellationTokenSource Cts { get; } = new();
    public CancellationToken Token => Cts.Token;

    public Session(string npcName)
    {
        NpcName = npcName;
    }

    public void Cancel() => Cts.Cancel();
}

public class AgentEndpoints
{
    public Endpoint Game { get; set; }
    public Endpoint LLM { get; set; }
    public Endpoint Npc { get; set; }
}