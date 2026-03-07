namespace LivingTown.State;

public sealed class TrackerStateSnapshot
{
    public Dictionary<string, NpcPersistentState> NpcStates { get; set; } = new();
    public EconomyTrackerState Economy { get; set; } = new();
}

public sealed class NpcPersistentState
{
    public int SocialFatigue { get; set; }
    public bool IsCreepedOut { get; set; }
    public int TotalDialogues { get; set; }
    public int TotalGifts { get; set; }
    public string? LastInteractionDayKey { get; set; }
}

public sealed class EconomyTrackerState
{
    public Dictionary<string, CropShipmentStat> Crops { get; set; } = new();
}

public sealed class CropShipmentStat
{
    public int ConsecutiveDays { get; set; }
    public int TotalDumped { get; set; }
    public int LastShippedDayId { get; set; } = -1;
}

public sealed class ShippingRecord
{
    public string ItemName { get; init; } = "";
    public int Quantity { get; init; }
}
