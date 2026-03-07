using LivingTown.State;

namespace LivingTown.Tests;

public class GameStateTrackerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _statePath;

    public GameStateTrackerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LivingTownTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _statePath = Path.Combine(_tempDirectory, "game-state.json");
    }

    [Fact]
    public void RecordDialogue_IncrementsCountersAndTrimsTopics()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        for (var i = 0; i < 6; i++)
            tracker.RecordDialogue("Sebastian", $"topic-{i}");

        var daily = tracker.GetDailyState("Sebastian");
        var persistent = tracker.GetPersistentState("Sebastian");

        Assert.Equal(6, daily.DialoguesToday);
        Assert.Equal(5, daily.RecentTopics.Count);
        Assert.Equal("topic-1", daily.RecentTopics[0]);
        Assert.Equal(6, persistent.TotalDialogues);
        Assert.Equal(6, persistent.SocialFatigue);
    }

    [Fact]
    public void RecordGift_UpdatesDailyAndPersistentState()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        tracker.RecordGift("Abigail", "Amethyst", GiftTaste.Love);

        var daily = tracker.GetDailyState("Abigail");
        var persistent = tracker.GetPersistentState("Abigail");

        Assert.Equal(1, daily.GiftsToday);
        Assert.Equal("Amethyst", daily.LastGiftItem);
        Assert.Equal(GiftTaste.Love, daily.LastGiftTaste);
        Assert.Equal(1, persistent.TotalGifts);
        Assert.Equal(5, persistent.SocialFatigue);
    }

    [Fact]
    public void EndDay_DecaysFatigueWhenNpcHadNoInteraction()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        tracker.RecordDialogue("Shane", "hey");
        tracker.EndDay();
        tracker.ResetDaily("Y1_Day2");
        tracker.EndDay();

        var persistent = tracker.GetPersistentState("Shane");

        Assert.Equal(0, persistent.SocialFatigue);
        Assert.False(persistent.IsCreepedOut);
    }

    [Fact]
    public void EndDay_DoesNotDecayFatigueWhenNpcHadInteraction()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        tracker.RecordDialogue("Emily", "first");
        tracker.EndDay();

        var persistent = tracker.GetPersistentState("Emily");

        Assert.Equal(1, persistent.SocialFatigue);
    }

    [Fact]
    public void ResetDaily_ClearsDailyStateButKeepsPersistentState()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        tracker.RecordDialogue("Haley", "hello");
        tracker.RecordGift("Haley", "Sunflower", GiftTaste.Like);
        tracker.ResetDaily("Y1_Day2");

        var daily = tracker.GetDailyState("Haley");
        var persistent = tracker.GetPersistentState("Haley");

        Assert.Equal(0, daily.DialoguesToday);
        Assert.Equal(0, daily.GiftsToday);
        Assert.Empty(daily.RecentTopics);
        Assert.Equal(1, persistent.TotalDialogues);
        Assert.Equal(1, persistent.TotalGifts);
        Assert.Equal(6, persistent.SocialFatigue);
    }

    [Fact]
    public void RecordShipping_TracksConsecutiveDaysAndTotals()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 100 } }, 10);
        tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 200 } }, 11);

        var crop = tracker.GetEconomyState().Crops["Blueberry"];

        Assert.Equal(2, crop.ConsecutiveDays);
        Assert.Equal(300, crop.TotalDumped);
        Assert.Equal(11, crop.LastShippedDayId);
    }

    [Fact]
    public void RecordShipping_ResetsConsecutiveDaysAfterGap()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);

        tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 100 } }, 10);
        tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 50 } }, 13);

        var crop = tracker.GetEconomyState().Crops["Blueberry"];

        Assert.Equal(1, crop.ConsecutiveDays);
        Assert.Equal(150, crop.TotalDumped);
    }

    [Fact]
    public void State_IsReloadedFromDisk()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);
        tracker.RecordDialogue("Sam", "band practice tonight");
        tracker.RecordGift("Sam", "Pizza", GiftTaste.Love);
        tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 42 } }, 7);

        var reloaded = new GameStateTracker(storagePath: _statePath);
        var sam = reloaded.GetPersistentState("Sam");
        var crop = reloaded.GetEconomyState().Crops["Blueberry"];

        Assert.Equal(1, sam.TotalDialogues);
        Assert.Equal(1, sam.TotalGifts);
        Assert.Equal(6, sam.SocialFatigue);
        Assert.Equal(42, crop.TotalDumped);
    }

    [Fact]
    public void GetStateForPrompt_IncludesDailyAndPersistentSummary()
    {
        var tracker = new GameStateTracker(storagePath: _statePath);
        tracker.RecordDialogue("Sebastian", "Do you still work on your bike?");
        tracker.RecordGift("Sebastian", "Sashimi", GiftTaste.Like);

        var summary = tracker.GetStateForPrompt("Sebastian");

        Assert.Contains("talked to me 1 time", summary);
        Assert.Contains("gift(s) today", summary);
        Assert.Contains("Social fatigue: 6", summary);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
