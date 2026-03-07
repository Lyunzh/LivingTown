using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using LivingTown.State;

namespace LivingTown.Game;

/// <summary>
/// Bridges real SMAPI input/inventory events into confirmed NPC gift events.
/// A gift is recorded only after the player targeted an NPC with an action button
/// and the inventory change confirms the offered item was actually consumed.
/// </summary>
public sealed class GiftEventBridge
{
    private readonly IMonitor _monitor;
    private readonly GameStateTracker _stateTracker;
    private readonly HeuristicWatchdog _watchdog;
    private readonly MemoryManager _memoryManager;

    private PendingGift? _pendingGift;

    public GiftEventBridge(
        IMonitor monitor,
        GameStateTracker stateTracker,
        HeuristicWatchdog watchdog,
        MemoryManager memoryManager)
    {
        _monitor = monitor;
        _stateTracker = stateTracker;
        _watchdog = watchdog;
        _memoryManager = memoryManager;
    }

    public void OnButtonPressed(ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree || !e.Button.IsActionButton())
            return;

        if (Game1.player.ActiveObject is not StardewValley.Object activeObject)
            return;

        if (!activeObject.canBeGivenAsGift())
            return;

        var npc = Game1.currentLocation?.isCharacterAtTile(e.Cursor.GrabTile);
        if (npc == null || !npc.CanReceiveGifts())
            return;

        var taste = ToGiftTaste(npc.getGiftTasteForThisItem(activeObject));
        _pendingGift = new PendingGift(
            npc.Name,
            activeObject.QualifiedItemId,
            activeObject.DisplayName,
            taste,
            Environment.TickCount64);

        _monitor.Log($"[GiftBridge] Pending gift captured: {activeObject.DisplayName} -> {npc.Name}", LogLevel.Trace);
    }

    public void OnInventoryChanged(InventoryChangedEventArgs e)
    {
        if (!e.IsLocalPlayer || _pendingGift == null)
            return;

        if (!WasPendingGiftConsumed(e, _pendingGift))
            return;

        var gift = _pendingGift;
        _pendingGift = null;

        _stateTracker.RecordGift(gift.NpcName, gift.ItemName, gift.Taste);
        _memoryManager.RecordEvent(gift.NpcName, $"Player gave me {gift.ItemName}.", importance: 6);

        var eventType = HeuristicWatchdog.ClassifyGift(gift.Taste);
        _watchdog.Evaluate(gift.NpcName, eventType);

        _monitor.Log($"[GiftBridge] Confirmed gift: {gift.ItemName} -> {gift.NpcName} ({gift.Taste})", LogLevel.Info);
    }

    public void OnUpdateTicked(UpdateTickedEventArgs e)
    {
        if (_pendingGift == null)
            return;

        if (Environment.TickCount64 - _pendingGift.StartTick <= 500)
            return;

        _monitor.Log($"[GiftBridge] Expired pending gift for {_pendingGift.NpcName}.", LogLevel.Trace);
        _pendingGift = null;
    }

    private static bool WasPendingGiftConsumed(InventoryChangedEventArgs e, PendingGift gift)
    {
        if (e.Removed.Any(item => item?.QualifiedItemId == gift.QualifiedItemId))
            return true;

        return e.QuantityChanged.Any(change =>
            change.Item?.QualifiedItemId == gift.QualifiedItemId && change.NewSize < change.OldSize);
    }

    private static GiftTaste ToGiftTaste(int rawTaste) => rawTaste switch
    {
        NPC.gift_taste_love => GiftTaste.Love,
        7 => GiftTaste.Love,
        NPC.gift_taste_like => GiftTaste.Like,
        NPC.gift_taste_dislike => GiftTaste.Dislike,
        NPC.gift_taste_hate => GiftTaste.Hate,
        _ => GiftTaste.Neutral
    };

    private sealed record PendingGift(
        string NpcName,
        string QualifiedItemId,
        string ItemName,
        GiftTaste Taste,
        long StartTick);
}

