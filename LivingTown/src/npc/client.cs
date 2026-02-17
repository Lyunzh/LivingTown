using System.Text;
using LivingTown.Pipeline;

namespace LivingTown.Npc;

/// <summary>
/// NPC client interface. Per-NPC instance with personality and memory.
/// OnMessage returns IAsyncEnumerable to support streaming output.
/// </summary>
public interface INpcClient
{
    /// <summary>The NPC name this client represents.</summary>
    string NpcName { get; }

    /// <summary>
    /// Process an incoming message and yield action messages.
    /// Async enumerable allows streaming: yield StreamText as tokens arrive.
    /// </summary>
    IAsyncEnumerable<object> OnMessageAsync(object msg, CancellationToken ct = default);
}

/// <summary>
/// Simple NPC client: conversation memory + streaming LLM consumption.
/// When receiving a StreamingResponse, consumes the token stream and
/// emits periodic StreamText updates for progressive display.
/// </summary>
public class SimpleNpcClient : INpcClient
{
    public string NpcName { get; }

    private readonly List<string> _conversationHistory = new();
    private int _dialogRound = 0;

    // How many characters to accumulate before emitting a StreamText update
    private const int StreamBatchSize = 15;

    public SimpleNpcClient(string npcName)
    {
        NpcName = npcName;
    }

    public async IAsyncEnumerable<object> OnMessageAsync(
        object msg,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        switch (msg)
        {
            case GameMsg.PlayerChat chat:
            {
                // Player spoke → record in history, request LLM response
                _dialogRound++;
                _conversationHistory.Add($"Player: {chat.Message}");

                // Build prompt from conversation history
                var prompt = BuildPrompt(chat.Message);

                yield return new NpcMsg.RequestLLM(NpcName, prompt, _dialogRound);
                break;
            }

            case LLMMsg.StreamingResponse streaming:
            {
                // Consume the live stream, emit periodic StreamText updates
                var accumulated = new StringBuilder();
                var lastEmittedLength = 0;

                await foreach (var token in streaming.TokenStream.WithCancellation(ct))
                {
                    accumulated.Append(token);

                    // Emit a StreamText update every BatchSize chars
                    if (accumulated.Length - lastEmittedLength >= StreamBatchSize)
                    {
                        yield return new NpcMsg.StreamText(
                            NpcName, accumulated.ToString(), IsComplete: false);
                        lastEmittedLength = accumulated.Length;
                    }
                }

                // Final emission with complete text
                var fullText = accumulated.ToString();
                _conversationHistory.Add($"{NpcName}: {fullText}");

                yield return new NpcMsg.StreamText(NpcName, fullText, IsComplete: true);
                break;
            }

            case GameMsg.TimeChange time:
            {
                if (time.Time > 2000)
                    yield return new NpcMsg.Move(NpcName, "Home");
                break;
            }
        }
    }

    /// <summary>
    /// Build the prompt from recent conversation history.
    /// Only sends the player's message — system prompt and history
    /// are managed by the LLMClient's message list.
    /// </summary>
    private string BuildPrompt(string playerMessage)
    {
        // We keep conversation context in the LLMClient's message history,
        // so we only need to send the latest player message as the prompt.
        return playerMessage;
    }
}