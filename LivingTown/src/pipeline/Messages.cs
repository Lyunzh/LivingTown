namespace LivingTown.Pipeline;

// =============================================================================
// Game Messages (sensor input from the game)
// =============================================================================
public static class GameMsg
{
    public record PlayerChat(string NpcName, string PlayerName, string Message);
    public record TimeChange(int Time);
    public record LocationChange(string NpcName, string NewLocation);
}

// =============================================================================
// LLM Messages (communication with the LLM service)
// =============================================================================
public static class LLMMsg
{
    // In: Pipeline → LLM Agent (request to generate response)
    public record Request(string NpcName, string Prompt, int DialogRound);

    // Out: LLM Agent → Pipeline (carries a live stream object!)
    // The stream is consumed by the NPC agent, not the pipeline.
    public record StreamingResponse(
        string NpcName,
        int DialogRound,
        IAsyncEnumerable<string> TokenStream
    );
}

// =============================================================================
// NPC Messages (NPC agent decisions/actions)
// =============================================================================
public static class NpcMsg
{
    // Out: NPC Agent → Pipeline (requesting LLM call)
    public record RequestLLM(string NpcName, string Prompt, int DialogRound);

    // Out: NPC Agent → Pipeline (streaming text for progressive display)
    // PartialText is the accumulated text so far, not just the latest token.
    public record StreamText(string NpcName, string PartialText, bool IsComplete);

    // Out: NPC Agent → Pipeline (actions for the game)
    public record Speak(string NpcName, string Text);
    public record Move(string NpcName, string Location);
    public record Emote(string NpcName, int EmoteId);
}
