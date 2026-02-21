namespace LivingTown.GOAP;

/// <summary>
/// A GOAP action: an atomic behavior an NPC can perform.
/// Each action has preconditions (world/NPC state required to start)
/// and effects (state changes after completion).
///
/// The Planner uses backward A* search: starting from the goal state,
/// it finds which actions' EFFECTS can produce the goal, then checks
/// if their PRECONDITIONS are met (or need sub-goals).
/// </summary>
public class GOAPAction
{
    /// <summary>Unique action identifier.</summary>
    public string Name { get; set; } = "";

    /// <summary>Cost of this action (lower = preferred by planner).</summary>
    public float Cost { get; set; } = 1f;

    /// <summary>State conditions that must be true BEFORE this action can run.</summary>
    public Dictionary<string, object> Preconditions { get; set; } = new();

    /// <summary>State changes that become true AFTER this action completes.</summary>
    public Dictionary<string, object> Effects { get; set; } = new();

    /// <summary>
    /// Estimated duration in game-minutes. Used for scheduling.
    /// 0 = instant (e.g., emote). 60 = takes about an hour.
    /// </summary>
    public int DurationMinutes { get; set; } = 10;

    /// <summary>Optional: the target location this action requires the NPC to be at.</summary>
    public string? RequiredLocation { get; set; }

    /// <summary>Check if all preconditions are satisfied by the given state.</summary>
    public bool ArePreconditionsMet(Dictionary<string, object> currentState)
    {
        foreach (var pre in Preconditions)
        {
            if (!currentState.TryGetValue(pre.Key, out var val))
                return false;
            if (!val.Equals(pre.Value))
                return false;
        }
        return true;
    }

    /// <summary>Apply this action's effects to a state (returns a new state dict).</summary>
    public Dictionary<string, object> ApplyEffects(Dictionary<string, object> currentState)
    {
        var newState = new Dictionary<string, object>(currentState);
        foreach (var eff in Effects)
            newState[eff.Key] = eff.Value;
        return newState;
    }
}

/// <summary>
/// Pre-built action library for Stardew Valley NPCs.
/// These are the atomic building blocks the Planner can compose.
/// </summary>
public static class ActionLibrary
{
    public static List<GOAPAction> GetDefaultActions() => new()
    {
        new GOAPAction
        {
            Name = "WalkTo_Saloon",
            Cost = 2f,
            Preconditions = { ["Saloon_IsOpen"] = true },
            Effects = { ["CurrentLocation"] = "Saloon" },
            RequiredLocation = null, // Can start from anywhere
            DurationMinutes = 15
        },
        new GOAPAction
        {
            Name = "WalkTo_Mountain",
            Cost = 3f,
            Preconditions = { },
            Effects = { ["CurrentLocation"] = "Mountain" },
            DurationMinutes = 20
        },
        new GOAPAction
        {
            Name = "WalkTo_Beach",
            Cost = 3f,
            Preconditions = { },
            Effects = { ["CurrentLocation"] = "Beach" },
            DurationMinutes = 20
        },
        new GOAPAction
        {
            Name = "WalkTo_Town",
            Cost = 1f,
            Preconditions = { },
            Effects = { ["CurrentLocation"] = "Town" },
            DurationMinutes = 10
        },
        new GOAPAction
        {
            Name = "WalkTo_Home",
            Cost = 1f,
            Preconditions = { },
            Effects = { ["CurrentLocation"] = "Home" },
            DurationMinutes = 10
        },
        new GOAPAction
        {
            Name = "BuyFood_Saloon",
            Cost = 2f,
            Preconditions = { ["CurrentLocation"] = "Saloon", ["Saloon_IsOpen"] = true },
            Effects = { ["HasFood"] = true },
            DurationMinutes = 5
        },
        new GOAPAction
        {
            Name = "Eat",
            Cost = 1f,
            Preconditions = { ["HasFood"] = true },
            Effects = { ["IsHungry"] = false, ["HasFood"] = false },
            DurationMinutes = 10
        },
        new GOAPAction
        {
            Name = "SitAndBrood",
            Cost = 1f,
            Preconditions = { ["CurrentLocation"] = "Mountain" },
            Effects = { ["Mood"] = "Calm", ["IsAngry"] = false },
            DurationMinutes = 30
        },
        new GOAPAction
        {
            Name = "PlayPool",
            Cost = 2f,
            Preconditions = { ["CurrentLocation"] = "Saloon" },
            Effects = { ["Mood"] = "Happy", ["IsBored"] = false },
            DurationMinutes = 30
        },
        new GOAPAction
        {
            Name = "TalkTo_Friend",
            Cost = 1.5f,
            Preconditions = { },
            Effects = { ["IsLonely"] = false, ["Mood"] = "Content" },
            DurationMinutes = 15
        },
        new GOAPAction
        {
            Name = "GoToSleep",
            Cost = 0.5f,
            Preconditions = { ["CurrentLocation"] = "Home" },
            Effects = { ["IsTired"] = false },
            DurationMinutes = 0 // Ends the NPC's day
        }
    };
}
