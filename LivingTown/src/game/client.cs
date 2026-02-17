using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingTown.Game;

/// <summary>
/// Game client interface. Per-NPC game interaction handler.
/// Different NPC types may require different game interaction strategies.
/// </summary>
public interface IGameClient
{
    /// <summary>The NPC name this client handles.</summary>
    string NpcName { get; }

    /// <summary>Show NPC speaking text as a speech bubble + chatbox message.</summary>
    void ExecuteSpeak(string text);

    /// <summary>Execute a move action in-game.</summary>
    void ExecuteMove(string location);

    /// <summary>Execute an emote action in-game.</summary>
    void ExecuteEmote(int emoteId);
}

/// <summary>
/// Default game client that uses Stardew Valley APIs to execute actions.
/// Uses showTextAboveHead for speech bubbles and chatBox for chat log.
/// </summary>
public class GameClient : IGameClient
{
    public string NpcName { get; }

    public GameClient(string npcName)
    {
        NpcName = npcName;
    }

    public void ExecuteSpeak(string text)
    {
        var npc = Game1.getCharacterFromName(NpcName);
        if (npc == null) return;

        // Show speech bubble above NPC head (non-blocking, auto-fades)
        npc.showTextAboveHead(text, duration: 3000);

        // Also show in chatbox for persistence
        Game1.chatBox?.addMessage(
            $"{NpcName}: {text}",
            new Color(220, 220, 100) // Yellow tint for NPC messages
        );
    }

    public void ExecuteMove(string location)
    {
        // TODO: NPC pathfinding implementation
    }

    public void ExecuteEmote(int emoteId)
    {
        var npc = Game1.getCharacterFromName(NpcName);
        npc?.doEmote(emoteId);
    }
}