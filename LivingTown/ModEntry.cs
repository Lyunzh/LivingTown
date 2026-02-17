using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using LivingTown.Pipeline;

namespace LivingTown;

public class ModEntry : Mod
{
    private Pipeline.Pipeline _pipeline = null!;
    private Game.Agent _gameAgent = null!;

    // Active sessions per NPC
    private readonly Dictionary<string, Session> _npcSessions = new();

    // Streaming state: tracks partial text being displayed per NPC
    private readonly ConcurrentDictionary<string, string> _streamingText = new();

    public override void Entry(IModHelper helper)
    {
        // 1. Create Agents (global singletons)
        _gameAgent = new Game.Agent(Monitor);
        var llmAgent = new LLM.Agent(Monitor);
        var npcAgent = new Npc.Agent(Monitor);

        // 2. Create Pipeline
        _pipeline = new Pipeline.Pipeline(Monitor, _gameAgent, llmAgent, npcAgent);

        // 3. Hook SMAPI events
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Input.ButtonPressed += OnButtonPressed;

        Monitor.Log("[ModEntry] LivingTown mod initialized. Press T near an NPC to chat.", LogLevel.Info);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Monitor.Log("[ModEntry] GameLaunched — Pipeline ready.", LogLevel.Info);
    }

    /// <summary>
    /// Called every game tick. Polls the GameAgent for pending actions.
    /// </summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady) return;

        foreach (var action in _gameAgent.PollActions())
        {
            Monitor.Log($"[ModEntry] Polled action: {action.GetType().Name}", LogLevel.Trace);

            switch (action)
            {
                case NpcMsg.StreamText stream:
                    HandleStreamText(stream);
                    break;

                case NpcMsg.Speak speak:
                    ExecuteSpeak(speak.NpcName, speak.Text);
                    break;

                case NpcMsg.Move move:
                    Monitor.Log($"[ModEntry] Move action for {move.NpcName} → {move.Location} (not implemented)", LogLevel.Debug);
                    break;

                case NpcMsg.Emote emote:
                    Monitor.Log($"[ModEntry] Emote {emote.EmoteId} for {emote.NpcName}", LogLevel.Debug);
                    Game1.getCharacterFromName(emote.NpcName)?.doEmote(emote.EmoteId);
                    break;
            }
        }
    }

    /// <summary>
    /// Handle progressive streaming text display.
    /// </summary>
    private void HandleStreamText(NpcMsg.StreamText stream)
    {
        if (stream.IsComplete)
        {
            _streamingText.TryRemove(stream.NpcName, out _);
            Monitor.Log($"[ModEntry] Stream COMPLETE for {stream.NpcName}: \"{stream.PartialText}\"", LogLevel.Info);

            var npc = Game1.getCharacterFromName(stream.NpcName);
            if (npc != null)
            {
                var bubbleText = stream.PartialText.Length > 60
                    ? stream.PartialText[..57] + "..."
                    : stream.PartialText;
                npc.showTextAboveHead(bubbleText, duration: 4000);
            }

            Game1.chatBox?.addMessage(
                $"{stream.NpcName}: {stream.PartialText}",
                new Color(220, 220, 100)
            );
        }
        else
        {
            var prevLen = _streamingText.GetValueOrDefault(stream.NpcName, "")?.Length ?? 0;
            _streamingText[stream.NpcName] = stream.PartialText;
            Monitor.Log($"[ModEntry] Stream partial for {stream.NpcName}: {stream.PartialText.Length} chars", LogLevel.Trace);

            if (stream.PartialText.Length - prevLen >= 10 || stream.PartialText.Length <= 15)
            {
                Game1.chatBox?.addInfoMessage(
                    $"{stream.NpcName} is typing: {stream.PartialText}▌"
                );
            }
        }
    }

    private void ExecuteSpeak(string npcName, string text)
    {
        Monitor.Log($"[ModEntry] ExecuteSpeak: {npcName} → \"{text}\"", LogLevel.Info);
        var npc = Game1.getCharacterFromName(npcName);
        if (npc == null)
        {
            Monitor.Log($"[ModEntry] WARNING: NPC '{npcName}' not found!", LogLevel.Warn);
            return;
        }

        var bubbleText = text.Length > 60 ? text[..57] + "..." : text;
        npc.showTextAboveHead(bubbleText, duration: 4000);

        Game1.chatBox?.addMessage(
            $"{npcName}: {text}",
            new Color(220, 220, 100)
        );
    }

    /// <summary>
    /// Press T near an NPC → open chat input menu.
    /// </summary>
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree) return;

        // Only trigger on T key
        if (e.Button != SButton.T) return;

        Monitor.Log("[ModEntry] T key pressed — looking for nearby NPC...", LogLevel.Debug);

        // Find NPC at cursor position
        var cursorTile = e.Cursor.GrabTile;
        var clickedNpc = Game1.currentLocation?.isCharacterAtTile(cursorTile);

        if (clickedNpc == null)
        {
            Monitor.Log($"[ModEntry] No NPC found at tile ({cursorTile.X}, {cursorTile.Y})", LogLevel.Debug);
            return;
        }

        var npcName = clickedNpc.Name;
        Monitor.Log($"[ModEntry] Found NPC: {npcName} — opening chat input.", LogLevel.Info);

        // Open chat input menu
        Game1.activeClickableMenu = new Game.ChatInputMenu(npcName, OnPlayerChatSubmit);
    }

    /// <summary>
    /// Callback when player submits a message.
    /// </summary>
    private void OnPlayerChatSubmit(string npcName, string message)
    {
        Monitor.Log($"[ModEntry] Player submitted to {npcName}: \"{message}\"", LogLevel.Info);

        // Show player message in chatbox (light blue)
        Game1.chatBox?.addMessage(
            $"You: {message}",
            new Color(150, 220, 255)
        );

        // Ensure session exists
        if (!_npcSessions.ContainsKey(npcName))
        {
            var session = new Session(npcName);
            _npcSessions[npcName] = session;
            _pipeline.Serve(session);
            Monitor.Log($"[ModEntry] NEW session created for NPC: {npcName} (ID: {session.Id})", LogLevel.Info);
        }

        // Publish chat → pipeline
        var sessionId = _npcSessions[npcName].Id;
        _gameAgent.PublishEvent(sessionId, new GameMsg.PlayerChat(npcName, "Player", message));
        Monitor.Log($"[ModEntry] PlayerChat published to pipeline for session {sessionId}", LogLevel.Debug);
    }
}
