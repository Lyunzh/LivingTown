using LivingTown.GOAP;
using LivingTown.LLM.Core;
using LivingTown.State;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

static class Program
{
    static int Main()
    {
        var failures = new List<string>();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "LivingTownTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            Run(failures, "Dialogue counters and topic trimming", () =>
            {
                var tracker = CreateTracker(tempDirectory, "dialogue");
                for (var i = 0; i < 6; i++)
                    tracker.RecordDialogue("Sebastian", $"topic-{i}");

                var daily = tracker.GetDailyState("Sebastian");
                var persistent = tracker.GetPersistentState("Sebastian");
                Expect(daily.DialoguesToday == 6, "dialogue count mismatch");
                Expect(daily.RecentTopics.Count == 5, "recent topic limit mismatch");
                Expect(daily.RecentTopics[0] == "topic-1", "oldest topic not trimmed");
                Expect(persistent.TotalDialogues == 6, "persistent dialogue total mismatch");
                Expect(persistent.SocialFatigue == 6, "social fatigue from dialogue mismatch");
            });

            Run(failures, "Gift tracking", () =>
            {
                var tracker = CreateTracker(tempDirectory, "gift");
                tracker.RecordGift("Abigail", "Amethyst", GiftTaste.Love);

                var daily = tracker.GetDailyState("Abigail");
                var persistent = tracker.GetPersistentState("Abigail");
                Expect(daily.GiftsToday == 1, "gift count mismatch");
                Expect(daily.LastGiftItem == "Amethyst", "last gift mismatch");
                Expect(daily.LastGiftTaste == GiftTaste.Love, "gift taste mismatch");
                Expect(persistent.TotalGifts == 1, "persistent gift total mismatch");
                Expect(persistent.SocialFatigue == 5, "gift fatigue mismatch");
            });

            Run(failures, "No-interaction day decays fatigue", () =>
            {
                var tracker = CreateTracker(tempDirectory, "decay");
                tracker.RecordDialogue("Shane", "hey");
                tracker.EndDay();
                tracker.ResetDaily("Y1_Day2");
                tracker.EndDay();

                var persistent = tracker.GetPersistentState("Shane");
                Expect(persistent.SocialFatigue == 0, "fatigue should decay to zero");
                Expect(!persistent.IsCreepedOut, "creeped-out flag should be false");
            });

            Run(failures, "Daily reset keeps persistent data", () =>
            {
                var tracker = CreateTracker(tempDirectory, "reset");
                tracker.RecordDialogue("Haley", "hello");
                tracker.RecordGift("Haley", "Sunflower", GiftTaste.Like);
                tracker.ResetDaily("Y1_Day2");

                var daily = tracker.GetDailyState("Haley");
                var persistent = tracker.GetPersistentState("Haley");
                Expect(daily.DialoguesToday == 0, "daily dialogue reset failed");
                Expect(daily.GiftsToday == 0, "daily gift reset failed");
                Expect(daily.RecentTopics.Count == 0, "daily topics reset failed");
                Expect(persistent.TotalDialogues == 1, "persistent dialogue should remain");
                Expect(persistent.TotalGifts == 1, "persistent gifts should remain");
                Expect(persistent.SocialFatigue == 6, "persistent fatigue should remain");
            });

            Run(failures, "Shipping accumulation and streaks", () =>
            {
                var tracker = CreateTracker(tempDirectory, "shipping");
                tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 100 } }, 10);
                tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 200 } }, 11);
                tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Blueberry", Quantity = 50 } }, 13);

                var crop = tracker.GetEconomyState().Crops["Blueberry"];
                Expect(crop.ConsecutiveDays == 1, "shipment streak should reset after day gap");
                Expect(crop.TotalDumped == 350, "shipment total mismatch");
                Expect(crop.LastShippedDayId == 13, "last shipped day mismatch");
            });

            Run(failures, "Persistent state reload", () =>
            {
                var tracker = CreateTracker(tempDirectory, "reload");
                tracker.RecordDialogue("Sam", "band practice tonight");
                tracker.RecordGift("Sam", "Pizza", GiftTaste.Love);
                tracker.RecordShipping(new[] { new ShippingRecord { ItemName = "Corn", Quantity = 42 } }, 7);

                var reloaded = new GameStateTracker(storagePath: Path.Combine(tempDirectory, "reload", "game-state.json"));
                var sam = reloaded.GetPersistentState("Sam");
                var crop = reloaded.GetEconomyState().Crops["Corn"];
                Expect(sam.TotalDialogues == 1, "reloaded dialogue total mismatch");
                Expect(sam.TotalGifts == 1, "reloaded gift total mismatch");
                Expect(sam.SocialFatigue == 6, "reloaded fatigue mismatch");
                Expect(crop.TotalDumped == 42, "reloaded crop total mismatch");
            });

            Run(failures, "Prompt summary includes daily and persistent state", () =>
            {
                var tracker = CreateTracker(tempDirectory, "summary");
                tracker.RecordDialogue("Sebastian", "Do you still work on your bike?");
                tracker.RecordGift("Sebastian", "Sashimi", GiftTaste.Like);
                var summary = tracker.GetStateForPrompt("Sebastian");

                Expect(summary.Contains("talked to me 1 time"), "summary missing dialogue count");
                Expect(summary.Contains("gift(s) today"), "summary missing gift count");
                Expect(summary.Contains("Social fatigue: 6"), "summary missing fatigue");
            });

            Run(failures, "Nightly memory compaction promotes important events", () =>
            {
                var memory = CreateMemoryManager(tempDirectory, "memory_compact", 12);
                memory.RecordEvent("Sebastian", "Player brought frozen tear to the mountain.", 8);
                memory.RecordEvent("Sebastian", "Player said hi.", 2);
                memory.RecordEvent("Sebastian", "Player mentioned motorcycle repairs.", 6);
                memory.RunNightlyMaintenance(12);

                var longTerm = memory.GetAllLongTermMemories("Sebastian");
                Expect(longTerm.Count == 2, "only important memories should persist long-term");
                Expect(memory.GetBufferCount("Sebastian") == 0, "short-term buffer should clear after nightly compaction");
            });

            Run(failures, "Long-term memory query prefers keyword matches", () =>
            {
                var memory = CreateMemoryManager(tempDirectory, "memory_query", 20);
                memory.RecordEvent("Sebastian", "Player talked about frogs and rain by the lake.", 7);
                memory.RecordEvent("Sebastian", "Player asked about pizza at the saloon.", 6);
                memory.RunNightlyMaintenance(20);

                var relevant = memory.GetRelevantLongTermMemories("Sebastian", 20, "frogs in the rain", 1);
                Expect(relevant.Count == 1, "relevant memory should be returned");
                Expect(relevant[0].Fact.Contains("frogs"), "keyword-matched memory should rank first");
            });

            Run(failures, "Long-term memory decays and permanent memories survive", () =>
            {
                var memory = CreateMemoryManager(tempDirectory, "memory_decay", 1);
                memory.RecordEvent("Abigail", "Player forgot my birthday.", 5);
                memory.RecordEvent("Abigail", "Player and I got married.", 10);
                memory.RunNightlyMaintenance(1);
                memory.RunNightlyMaintenance(120);

                var longTerm = memory.GetAllLongTermMemories("Abigail");
                Expect(longTerm.Count == 1, "decayed non-permanent memories should be pruned");
                Expect(longTerm[0].Permanent, "permanent memory should survive decay");
            });

            Run(failures, "Watchdog escalates only after entropy threshold", () =>
            {
                var watchdog = new HeuristicWatchdog();
                var verdict1 = watchdog.Evaluate("Emily", "Dialogue_First");
                var verdict2 = watchdog.Evaluate("Emily", "Dialogue_Repeat");
                var verdict3 = watchdog.Evaluate("Emily", "Dialogue_Complex");
                var verdict4 = watchdog.Evaluate("Emily", "Gift_Love");

                Expect(verdict1 == EscalationVerdict.ShortCircuit, "first low-entropy event should short-circuit");
                Expect(verdict2 == EscalationVerdict.ShortCircuit, "repeat low-entropy event should short-circuit");
                Expect(verdict3 == EscalationVerdict.ShortCircuit, "threshold should not trigger too early");
                Expect(verdict4 == EscalationVerdict.Escalate, "high cumulative entropy should escalate");
            });

            RunAsync(failures, "Game tools bind to current NPC context", async () =>
            {
                var monitor = new NullMonitor();
                var registry = new ToolRegistry(monitor);
                var blackboard = new Blackboard(monitor);
                string? rememberedNpc = null;
                string? rememberedFact = null;
                int rememberedImportance = 0;
                string? emoteNpc = null;
                int emoteId = 0;

                var setGoal = GameTools.SetGoal(monitor, registry, blackboard);
                var remember = GameTools.RememberFact(monitor, registry, (npc, fact, importance) =>
                {
                    rememberedNpc = npc;
                    rememberedFact = fact;
                    rememberedImportance = importance;
                });
                var playEmote = GameTools.PlayEmote(monitor, registry, (npc, emote) =>
                {
                    emoteNpc = npc;
                    emoteId = emote;
                });

                using var scope = registry.BeginContextScope(new Dictionary<string, string> { ["NPC_NAME"] = "Sebastian" });
                await setGoal.ExecuteAsync(JObject.Parse("{\"goal_name\":\"Mood=Calm\",\"priority\":\"high\"}"), CancellationToken.None);
                await remember.ExecuteAsync(JObject.Parse("{\"fact\":\"Player likes frogs\",\"importance\":\"8\"}"), CancellationToken.None);
                await playEmote.ExecuteAsync(JObject.Parse("{\"emote\":\"thinking\"}"), CancellationToken.None);

                var goal = blackboard.DequeueGoal();
                Expect(goal != null, "goal should be enqueued");
                Expect(goal!.NpcName == "Sebastian", "tool context should set current NPC");
                Expect(rememberedNpc == "Sebastian", "remember tool should bind NPC context");
                Expect(rememberedFact == "Player likes frogs", "remember tool should forward fact");
                Expect(rememberedImportance == 8, "remember tool should forward importance");
                Expect(emoteNpc == "Sebastian", "play_emote should bind NPC context");
                Expect(emoteId == 8, "thinking emote should map to question bubble id");
            }).GetAwaiter().GetResult();

            Run(failures, "GOAP command parser builds goal objects", () =>
            {
                var goal = GoapCommandParser.BuildGoal("Sebastian", "Mood=Calm", "high", "debug");
                Expect(goal.NpcName == "Sebastian", "goal parser should keep npc name");
                Expect(goal.GoalKey == "Mood", "goal parser should parse key");
                Expect(Equals(goal.GoalValue, "Calm"), "goal parser should parse string value");
                Expect(goal.Priority == GoalPriority.High, "goal parser should parse priority");
                Expect(goal.Reason == "debug", "goal parser should keep reason");

                var boolGoal = GoapCommandParser.BuildGoal("Shane", "IsHungry=false");
                Expect(Equals(boolGoal.GoalValue, false), "goal parser should coerce bool values");
            });

            Run(failures, "GOAP executor applies action effects to blackboard", () =>
            {
                var monitor = new NullMonitor();
                var blackboard = new Blackboard(monitor);
                string? thought = null;
                string? movedTo = null;
                var executor = new GoapActionExecutor(blackboard, monitor, (npc, location) => movedTo = location, null, (npc, text) => thought = text);
                var action = new GOAPAction
                {
                    Name = "Eat",
                    Effects = { ["IsHungry"] = false, ["HasFood"] = false }
                };

                executor.ExecuteAction("Shane", action);

                Expect(Equals(blackboard.GetNpcFact("Shane", "IsHungry"), false), "executor should update blackboard effects");
                Expect(Equals(blackboard.GetNpcFact("Shane", "HasFood"), false), "executor should apply all effects");
                Expect(thought == "Finally. Food.", "executor should emit visible feedback for eat action");

                var moveAction = new GOAPAction
                {
                    Name = "WalkTo_Saloon",
                    Effects = { ["CurrentLocation"] = "Saloon" }
                };
                executor.ExecuteAction("Shane", moveAction);
                Expect(movedTo == "Saloon", "walk action should invoke movement callback");
            });
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("All integration tests passed.");
            return 0;
        }

        Console.Error.WriteLine("Tests failed:");
        foreach (var failure in failures)
            Console.Error.WriteLine($"- {failure}");
        return 1;
    }

    static GameStateTracker CreateTracker(string root, string testName)
    {
        var directory = Path.Combine(root, testName);
        Directory.CreateDirectory(directory);
        return new GameStateTracker(storagePath: Path.Combine(directory, "game-state.json"));
    }

    static MemoryManager CreateMemoryManager(string root, string testName, int currentDay)
    {
        var directory = Path.Combine(root, testName);
        Directory.CreateDirectory(directory);
        return new MemoryManager(storagePath: Path.Combine(directory, "memory-state.json"), contextProvider: () => new MemoryContext(currentDay, "spring", currentDay));
    }

    static void Run(List<string> failures, string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.Message}");
        }
    }

    static async Task RunAsync(List<string> failures, string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.Message}");
        }
    }

    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

