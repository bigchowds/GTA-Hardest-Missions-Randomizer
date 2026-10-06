using Ghmr.Core;
using Ghmr.Core.Bridge;
using Ghmr.Core.Installation;
using Ghmr.Core.Launch;
using Ghmr.Core.Run;
using System.Text.Json.Nodes;

namespace Ghmr.Core.SelfTest;

internal static class Program
{
    private static int Main()
    {
        try
        {
            MissionCatalog catalog = MissionCatalog.Load(
                Path.Combine(AppContext.BaseDirectory, "config", "missions.v0.1.json"));
            TestV01MissionComposition(catalog);
            TestV01MissionSet(catalog);
            TestSanAndreasLaunchIndexes(catalog);
            TestGta3LaunchIndexes(catalog);
            TestViceCityLaunchIndex(catalog);
            TestGta4LaunchScript(catalog);
            TestBridgeLineLimit(catalog);
            TestCleoDefinitionPatcher();
            TestSafeGameHandoff();
            TestCompletedGtaVHandoff();
            TestFixedValidationPlan(catalog);
            TestFiveMissionBetaPlans(catalog);
            TestFiveGameValidationProgression(catalog);
            TestTenThousandLockedPlans(catalog);
            TestFailureRetryAndCompletion(catalog);
            TestActiveGameExit(catalog);
            TestPersistenceRedactsFutureOrder();
            Console.WriteLine("GHMR core self-test passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GHMR core self-test failed: {exception.Message}");
            return 1;
        }
    }

    private static void TestV01MissionComposition(MissionCatalog catalog)
    {
        Assert(catalog.Missions.Count == 15, "v0.1 must contain exactly 15 missions.");
        IGrouping<string, MissionDefinition>[] games = catalog.Missions
            .GroupBy(mission => mission.Game, StringComparer.Ordinal)
            .ToArray();
        Assert(games.Length == 5, "v0.1 must contain exactly five games.");
        foreach (IGrouping<string, MissionDefinition> game in games)
        {
            Assert(game.Count() == 3, $"{game.Key} must contribute exactly three missions.");
        }
    }

    private static void TestV01MissionSet(MissionCatalog catalog)
    {
        string[] expected =
        [
            "gta3.espresso_2_go",
            "gta3.sam",
            "gta3.the_exchange",
            "gta4.out_of_commission",
            "gta4.the_snow_storm",
            "gta4.three_leaf_clover",
            "gtav.derailed",
            "gtav.minor_turbulence",
            "gtav.the_big_score",
            "sa.end_of_the_line",
            "sa.supply_lines",
            "sa.wrong_side_of_the_tracks",
            "vc.death_row",
            "vc.demolition_man",
            "vc.the_driver"
        ];

        string[] actual = catalog.Missions
            .Select(mission => mission.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert(
            expected.SequenceEqual(actual),
            "v0.1 mission ids changed without updating the approved mission-set test.");
    }

    private static void TestTenThousandLockedPlans(MissionCatalog catalog)
    {
        string[] expected = catalog.Missions.Select(mission => mission.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> trilogyGames = new(
            ["gta3de", "vcde", "sade"],
            StringComparer.Ordinal);

        for (uint seed = 1; seed <= 10_000; seed++)
        {
            RunEngine engine = CreateEngine(catalog, out _, out _);
            RunSnapshot plan = engine.StartNewRun(mode: "development", seed: seed);
            string[] actual = plan.MissionOrder.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Assert(expected.SequenceEqual(actual), $"Seed {seed} changed the mission pool.");
            Assert(
                trilogyGames.Contains(catalog.GetMission(plan.MissionOrder[0]).Game),
                $"Seed {seed} did not begin with a Definitive Edition Trilogy mission.");

            RunEngine sameSeedEngine = CreateEngine(catalog, out _, out _);
            RunSnapshot sameSeedPlan = sameSeedEngine.StartNewRun(mode: "development", seed: seed);
            Assert(
                plan.MissionOrder.SequenceEqual(sameSeedPlan.MissionOrder),
                $"Seed {seed} was not deterministic.");
        }
    }

    private static void TestSanAndreasLaunchIndexes(MissionCatalog catalog)
    {
        Dictionary<string, int> expected = new(StringComparer.Ordinal)
        {
            ["sa.wrong_side_of_the_tracks"] = 29,
            ["sa.supply_lines"] = 73,
            ["sa.end_of_the_line"] = 110
        };

        foreach ((string id, int launchIndex) in expected)
        {
            MissionDefinition mission = catalog.GetMission(id);
            Assert(
                mission.LaunchIndex == launchIndex,
                $"{id} must use San Andreas mission index {launchIndex}.");
        }

        Assert(
            catalog.Missions
                .Where(mission => mission.Game is not ("sade" or "gta3de" or "vcde"))
                .All(mission => mission.LaunchIndex is null),
            "Unverified IV and V missions must not contain provisional launch indexes.");
    }

    private static void TestGta3LaunchIndexes(MissionCatalog catalog)
    {
        Dictionary<string, int> expected = new(StringComparer.Ordinal)
        {
            ["gta3.espresso_2_go"] = 72,
            ["gta3.sam"] = 73,
            ["gta3.the_exchange"] = 79
        };

        foreach ((string id, int launchIndex) in expected)
        {
            MissionDefinition mission = catalog.GetMission(id);
            Assert(
                mission.LaunchIndex == launchIndex,
                $"{id} must use GTA III mission index {launchIndex}.");
        }
    }

    private static void TestViceCityLaunchIndex(MissionCatalog catalog)
    {
        MissionDefinition demolitionMan = catalog.GetMission("vc.demolition_man");
        Assert(
            demolitionMan.LaunchIndex == 19,
            "vc.demolition_man must use Vice City mission index 19.");

        Assert(
            catalog.Missions
                .Where(mission => mission.Game == "vcde" &&
                    mission.Id != "vc.demolition_man")
                .All(mission => mission.LaunchIndex is null),
            "Untested Vice City missions must not contain launch indexes yet.");
    }

    private static void TestGta4LaunchScript(MissionCatalog catalog)
    {
        MissionDefinition threeLeafClover = catalog.GetMission(
            "gta4.three_leaf_clover");
        Assert(
            threeLeafClover.LaunchScript == "Packie3",
            "gta4.three_leaf_clover must use GTA IV script Packie3.");

        Assert(
            catalog.Missions
                .Where(mission => mission.Game == "gta4" &&
                    mission.Id != "gta4.three_leaf_clover")
                .All(mission => mission.LaunchScript is null),
            "Untested GTA IV missions must not contain launch scripts yet.");
    }

    private static void TestBridgeLineLimit(MissionCatalog catalog)
    {
        MissionDefinition longestMission = catalog.Missions
            .OrderByDescending(mission => mission.Id.Length)
            .First();
        BridgeEnvelope command = new()
        {
            Type = ControllerCommandTypes.PrepareMission,
            MessageId = $"controller-{long.MaxValue}",
            Game = longestMission.Game,
            BridgeSessionId = new string('a', 32),
            RunId = new string('b', 32),
            MissionId = longestMission.Id
        };
        string encoded = BridgeProtocol.Serialize(command);
        Assert(
            System.Text.Encoding.UTF8.GetByteCount(encoded) <=
            BridgeProtocol.MaxMessageUtf8Bytes,
            "A valid controller command exceeds the CLEO string transport limit.");

        string finalRelease = BridgeProtocol.Serialize(command with
        {
            Type = ControllerCommandTypes.ReleaseBridge,
            Reason = "done"
        });
        Assert(
            System.Text.Encoding.UTF8.GetByteCount(finalRelease) <=
            BridgeProtocol.MaxMessageUtf8Bytes,
            "The final no-fade release exceeds the CLEO string transport limit.");

        bool oversizedRejected = false;
        try
        {
            BridgeProtocol.Serialize(command with { Reason = new string('x', 300) });
        }
        catch (InvalidDataException)
        {
            oversizedRejected = true;
        }
        Assert(oversizedRejected, "An oversized bridge line was not rejected.");
    }

    private static void TestCleoDefinitionPatcher()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"ghmr-definition-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string definitionsPath = Path.Combine(directory, "unknown_x64.json");
            File.WriteAllText(
                definitionsPath,
                """
                {
                  "meta": { "version": "test" },
                  "extensions": [
                    { "name": "existing", "commands": [] }
                  ],
                  "classes": []
                }
                """);
            string extensionPath = Path.Combine(
                AppContext.BaseDirectory,
                "config",
                "cleo",
                "ghmr-ini-extension.v1.json");

            Assert(
                CleoDefinitionPatcher.Apply(definitionsPath, extensionPath),
                "The GHMR CLEO extension was not added.");
            Assert(
                !CleoDefinitionPatcher.Apply(definitionsPath, extensionPath),
                "Applying the same GHMR CLEO extension was not idempotent.");

            JsonObject patched = JsonNode.Parse(File.ReadAllText(definitionsPath))!.AsObject();
            JsonArray extensions = patched["extensions"]!.AsArray();
            Assert(extensions.Count == 2, "Patching removed or duplicated an extension.");
            Assert(
                extensions.OfType<JsonObject>().Any(extension =>
                    extension["name"]?.GetValue<string>() == "existing"),
                "Patching removed an unrelated CLEO extension.");
            Assert(
                extensions.OfType<JsonObject>().Any(extension =>
                    extension["name"]?.GetValue<string>() ==
                    CleoDefinitionPatcher.ExtensionName),
                "The GHMR CLEO extension is missing after patching.");

            Assert(
                CleoDefinitionPatcher.Remove(definitionsPath),
                "The GHMR CLEO extension was not removed.");
            Assert(
                !CleoDefinitionPatcher.Remove(definitionsPath),
                "Removing an absent GHMR CLEO extension was not idempotent.");
            JsonObject removed = JsonNode.Parse(File.ReadAllText(definitionsPath))!.AsObject();
            Assert(
                removed["extensions"]!.AsArray().Count == 1,
                "Removing GHMR changed unrelated CLEO extensions.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestFailureRetryAndCompletion(MissionCatalog catalog)
    {
        RunEngine engine = CreateEngine(catalog, out ManualClock clock, out MemoryPersistence store);
        RunSnapshot started = engine.StartNewRun(mode: "development", seed: 1234);
        MissionDefinition mission = engine.CurrentMission
            ?? throw new InvalidOperationException("No first mission.");
        string session = "test-session";

        EngineResult ready = engine.AcceptBridgeEvent(Event(
            BridgeEventTypes.BridgeReady,
            "ready",
            mission,
            session,
            started.RunId));
        Assert(ready.Action == ControllerAction.PrepareMission, "Bridge did not prepare mission.");

        EngineResult prepared = engine.AcceptBridgeEvent(Event(
            BridgeEventTypes.MissionPrepared,
            "prepared",
            mission,
            session,
            started.RunId));
        Assert(prepared.Action == ControllerAction.StartMission, "Prepared mission did not start.");

        engine.AcceptBridgeEvent(Event(
            BridgeEventTypes.PlayerControlGained,
            "control-1",
            mission,
            session,
            started.RunId));
        clock.Advance(TimeSpan.FromSeconds(2));

        BridgeEnvelope failure = Event(
            BridgeEventTypes.MissionFailed,
            "failure-1",
            mission,
            session,
            started.RunId);
        EngineResult failed = engine.AcceptBridgeEvent(failure);
        Assert(failed.Action == ControllerAction.RestartMission, "Failure did not request restart.");
        Assert(engine.CurrentMission?.Id == mission.Id, "Failure rerolled the mission.");
        Assert(engine.Snapshot.Failures == 1, "Failure count is wrong.");
        Assert(engine.Snapshot.GameplayElapsedMilliseconds == 2000, "Gameplay timer is wrong.");

        EngineResult duplicate = engine.AcceptBridgeEvent(failure);
        Assert(
            duplicate.Disposition == MessageDisposition.IgnoredDuplicate,
            "Duplicate failure was accepted.");
        Assert(engine.Snapshot.Failures == 1, "Duplicate failure changed the count.");

        engine.AcceptBridgeEvent(Event(
            BridgeEventTypes.PlayerControlGained,
            "control-2",
            mission,
            session,
            started.RunId));
        clock.Advance(TimeSpan.FromSeconds(1));
        EngineResult completed = engine.AcceptBridgeEvent(Event(
            BridgeEventTypes.MissionCompleted,
            "complete-1",
            mission,
            session,
            started.RunId));
        Assert(
            completed.Action is ControllerAction.LaunchNextMission or ControllerAction.PrepareMission,
            "Completion did not advance.");
        Assert(engine.Snapshot.CurrentIndex == 1, "Completion advanced by the wrong amount.");
        Assert(engine.Snapshot.GameplayElapsedMilliseconds == 3000, "Retry time was not accumulated.");
        Assert(store.Saves.Count > 0, "Run state was never persisted.");

        MissionDefinition next = engine.CurrentMission
            ?? throw new InvalidOperationException("No next mission.");
        EngineResult stale = engine.AcceptBridgeEvent(Event(
            BridgeEventTypes.PlayerControlGained,
            "stale",
            next,
            "old-session",
            started.RunId));
        Assert(stale.Disposition == MessageDisposition.Rejected, "Stale bridge session was accepted.");
    }

    private static void TestActiveGameExit(MissionCatalog catalog)
    {
        foreach (RunPhase phase in new[] { RunPhase.Preparing, RunPhase.Running, RunPhase.Restarting })
        {
            RunEngine engine = CreateEngine(catalog, out ManualClock clock, out MemoryPersistence store);
            RunSnapshot started = engine.StartValidationRun(["gta3.espresso_2_go"]);
            MissionDefinition mission = engine.CurrentMission!;
            const string session = "exit-test-session";
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.BridgeReady, "ready", mission, session, started.RunId));
            if (phase != RunPhase.Preparing)
            {
                engine.AcceptBridgeEvent(Event(BridgeEventTypes.MissionPrepared, "prepared", mission, session, started.RunId));
                engine.AcceptBridgeEvent(Event(BridgeEventTypes.PlayerControlGained, "control", mission, session, started.RunId));
                clock.Advance(TimeSpan.FromSeconds(2));
            }
            if (phase == RunPhase.Restarting)
                engine.AcceptBridgeEvent(Event(BridgeEventTypes.MissionFailed, "failed", mission, session, started.RunId));

            Assert(!engine.AbortIfActiveGameExited("old-run", 0, mission.Game, session, "exit"), "Old run exit was accepted.");
            Assert(!engine.AbortIfActiveGameExited(started.RunId, 1, mission.Game, session, "exit"), "Wrong mission index exit was accepted.");
            Assert(!engine.AbortIfActiveGameExited(started.RunId, 0, "sade", session, "exit"), "Wrong game exit was accepted.");
            Assert(!engine.AbortIfActiveGameExited(started.RunId, 0, mission.Game, "old-session", "exit"), "Old session exit was accepted.");
            Assert(engine.Snapshot.Phase == phase, "Rejected exit changed the active run.");
            int failures = engine.Snapshot.Failures;
            Assert(engine.AbortIfActiveGameExited(started.RunId, 0, mission.Game, session, "game closed"), "Active game exit did not stop the run.");
            RunSnapshot stopped = engine.Snapshot;
            Assert(stopped.Phase == RunPhase.Aborted && !stopped.GameplayTimerRunning &&
                stopped.ActiveBridgeSessionId is null, "Stopped run retained active gameplay/session state.");
            Assert(stopped.Failures == failures && stopped.CompletedMissionIds.Count == 0 &&
                stopped.CurrentIndex == 0, "Manual exit counted a failure/completion or advanced the plan.");
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert(engine.Snapshot.GameplayElapsedMilliseconds == stopped.GameplayElapsedMilliseconds &&
                engine.Snapshot.RealElapsedMilliseconds == stopped.RealElapsedMilliseconds, "Stopped timers kept advancing.");
            Assert(store.Saves[^1].Phase == RunPhase.Aborted, "Stopped run was not persisted.");

            RunSnapshot replacement = engine.StartValidationRun([mission.Id]);
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.BridgeReady, "new-ready", mission, session, replacement.RunId));
            Assert(!engine.AbortIfActiveGameExited(started.RunId, 0, mission.Game, session, "late exit"),
                "An old watcher stopped a replacement run.");
        }

        // Both cross-game and same-game completion can leave an old exit watcher.
        foreach (string nextId in new[] { "gta3.espresso_2_go", "sa.supply_lines" })
        {
            RunEngine engine = CreateEngine(catalog, out _, out _);
            RunSnapshot started = engine.StartValidationRun(["sa.wrong_side_of_the_tracks", nextId]);
            MissionDefinition first = engine.CurrentMission!;
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.BridgeReady, "ready", first, "first-session", started.RunId));
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.MissionPrepared, "prepared", first, "first-session", started.RunId));
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.PlayerControlGained, "control", first, "first-session", started.RunId));
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.MissionCompleted, "complete", first, "first-session", started.RunId));
            Assert(!engine.AbortIfActiveGameExited(started.RunId, 0, first.Game, "first-session", "normal handoff exit"),
                "A completed game's normal exit stopped the handoff.");
            MissionDefinition next = engine.CurrentMission!;
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.BridgeReady, "next-ready", next, "next-session", started.RunId));
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.MissionPrepared, "next-prepared", next, "next-session", started.RunId));
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.PlayerControlGained, "next-control", next, "next-session", started.RunId));
            engine.AcceptBridgeEvent(Event(BridgeEventTypes.MissionCompleted, "next-complete", next, "next-session", started.RunId));
            Assert(engine.Snapshot.Phase == RunPhase.Finished, "Exit regression setup did not finish.");
            Assert(!engine.AbortIfActiveGameExited(started.RunId, 1, next.Game, "next-session", "closed after finish") &&
                engine.Snapshot.Phase == RunPhase.Finished, "Closing the final game invalidated a finished run.");
        }
    }

    private static void TestSafeGameHandoff()
    {
        DictionaryProfileProvider profiles = new(
        [
            Profile("sade", "SanAndreas"),
            Profile("gta3de", "LibertyCity"),
            Profile("vcde", "ViceCity"),
            Profile("gta4", "GTAIV"),
            Profile("gtav_enhanced", "GTA5_Enhanced")
        ]);
        RecordingProcessManager processes = new();
        RecordingDelay delay = new(processes.Events);
        GameHandoffService handoff = new(
            profiles,
            processes,
            delay,
            fadeDelay: TimeSpan.FromMilliseconds(900),
            closeTimeout: TimeSpan.FromSeconds(20),
            launcherSettleDelay: TimeSpan.FromSeconds(3));

        string[] games = ["sade", "gta3de", "vcde", "gta4", "gtav_enhanced"];
        for (int index = 0; index < games.Length - 1; index++)
        {
            List<GameHandoffStage> progressStages = [];
            GameHandoffResult result = handoff
                .TransitionAsync(
                    games[index],
                    games[index + 1],
                    reportProgress: progress => progressStages.Add(progress.Stage))
                .GetAwaiter()
                .GetResult();
            Assert(
                result.Outcome == GameHandoffOutcome.Launched,
                $"The {games[index]} to {games[index + 1]} handoff did not launch.");
            Assert(
                progressStages.SequenceEqual(
                [
                    GameHandoffStage.ClosingCompletedGame,
                    GameHandoffStage.SettlingPlatform,
                    GameHandoffStage.LaunchingNextGame,
                    GameHandoffStage.NextGameDetected
                ]),
                $"The {games[index]} to {games[index + 1]} visual stages were out of order.");
        }
        Assert(
            processes.Events.SequenceEqual(
            [
                "delay:900",
                "close:sade:20000",
                "delay:3000",
                "launch:gta3de",
                "delay:900",
                "close:gta3de:20000",
                "delay:3000",
                "launch:vcde",
                "delay:900",
                "close:vcde:20000",
                "delay:3000",
                "launch:gta4",
                "delay:900",
                "close:gta4:20000",
                "delay:3000",
                "launch:gtav_enhanced"
            ]),
            "The five-game handoffs did not fade, close, settle and launch in order.");

        RecordingProcessManager missingProcesses = new();
        GameHandoffService missingHandoff = new(
            new DictionaryProfileProvider([Profile("sade", "SanAndreas")]),
            missingProcesses,
            new RecordingDelay(missingProcesses.Events));
        GameHandoffResult missing = missingHandoff
            .TransitionAsync("sade", "gta3de")
            .GetAwaiter()
            .GetResult();
        Assert(
            missing.Outcome == GameHandoffOutcome.ProfileMissing,
            "A missing next-game profile was not reported.");
        Assert(
            missingProcesses.Events.Count == 0,
            "The current game was touched before both profiles were verified.");

        RecordingProcessManager timedOutProcesses = new()
        {
            CloseOutcome = GameProcessCloseOutcome.TimedOut
        };
        GameHandoffService timedOutHandoff = new(
            profiles,
            timedOutProcesses,
            new RecordingDelay(timedOutProcesses.Events),
            fadeDelay: TimeSpan.Zero,
            closeTimeout: TimeSpan.FromSeconds(1));
        GameHandoffResult timedOut = timedOutHandoff
            .TransitionAsync("sade", "gta3de")
            .GetAwaiter()
            .GetResult();
        Assert(
            timedOut.Outcome == GameHandoffOutcome.CloseTimedOut,
            "A graceful-close timeout was not reported.");
        Assert(
            !timedOutProcesses.Events.Contains("launch:gta3de", StringComparer.Ordinal),
            "The next game launched while the completed game was still open.");
    }

    private static void TestCompletedGtaVHandoff()
    {
        DictionaryProfileProvider profiles = new(
            [Profile("gtav_enhanced", "GTA5_Enhanced"), Profile("gta3de", "LibertyCity")]);
        RecordingProcessManager processes = new();
        GameHandoffService handoff = new(profiles, processes, new RecordingDelay(processes.Events),
            fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
        GameHandoffResult result = handoff.TransitionAsync("gtav_enhanced", "gta3de").GetAwaiter().GetResult();
        Assert(result.Outcome == GameHandoffOutcome.Launched &&
            processes.Events.Contains("completed-close:gtav_enhanced:20000") &&
            !processes.Events.Any(value => value.StartsWith("close:", StringComparison.Ordinal)) &&
            processes.Events.Last() == "launch:gta3de",
            "Completed GTA V must use its dedicated close capability before launching GTA III.");
        processes.Events.Clear();
        handoff.FinishAsync("gtav_enhanced").GetAwaiter().GetResult();
        Assert(processes.Events.Contains("close:gtav_enhanced:20000") &&
            !processes.Events.Any(value => value.StartsWith("completed-close:", StringComparison.Ordinal)),
            "An ordinary final closure must never use the completed-replay termination path.");
        processes.Events.Clear();
        handoff.LaunchInitialAsync("gtav_enhanced").GetAwaiter().GetResult();
        Assert(processes.Events.SequenceEqual(["launch:gtav_enhanced"]),
            "Starting GTA V must not terminate any process.");

        RecordingProcessManager blocked = new() { CloseOutcome = GameProcessCloseOutcome.ManualCloseRequired };
        GameHandoffService blockedHandoff = new(profiles, blocked, new RecordingDelay(blocked.Events),
            fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
        Assert(blockedHandoff.TransitionAsync("gtav_enhanced", "gta3de").GetAwaiter().GetResult().Outcome ==
            GameHandoffOutcome.ManualCloseRequired &&
            !blocked.Events.Contains("launch:gta3de"),
            "An unsuccessful close must not launch the next game.");
        blocked.Events.Clear();
        blockedHandoff.ResumeAfterCompletedGameExitAsync("gtav_enhanced", "gta3de").GetAwaiter().GetResult();
        Assert(blocked.Events.SequenceEqual(["delay:0", "launch:gta3de"]),
            "Confirmed manual exit must resume launch without closing another GTA V process.");

        RecordingProcessManager missing = new();
        GameHandoffService incomplete = new(new DictionaryProfileProvider([Profile("gtav_enhanced", "GTA5_Enhanced")]),
            missing, new RecordingDelay(missing.Events));
        Assert(incomplete.TransitionAsync("gtav_enhanced", "gta3de").GetAwaiter().GetResult().Outcome ==
            GameHandoffOutcome.ProfileMissing && missing.Events.Count == 0,
            "An incomplete next-game configuration must not terminate GTA V.");
        processes.Events.Clear();
        using CancellationTokenSource stopped = new();
        stopped.Cancel();
        bool cancelled = false;
        try { handoff.TransitionAsync("gtav_enhanced", "gta3de", stopped.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled && !processes.Events.Any(value => value.StartsWith("completed-close:", StringComparison.Ordinal) ||
            value.StartsWith("launch:", StringComparison.Ordinal)),
            "A cancelled handoff must neither terminate GTA V nor launch the next game.");
        using CancellationTokenSource stopDuringSettle = new();
        RecordingProcessManager settling = new();
        GameHandoffService settlingHandoff = new(profiles, settling, new CancelOnSettleDelay(stopDuringSettle),
            fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
        cancelled = false;
        try { settlingHandoff.TransitionAsync("gtav_enhanced", "gta3de", stopDuringSettle.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled && settling.Events.Contains("completed-close:gtav_enhanced:20000") &&
            !settling.Events.Contains("launch:gta3de"),
            "Stopping after completed GTA V exits must still prevent the next-game launch.");
    }

    private static void TestFixedValidationPlan(MissionCatalog catalog)
    {
        string[] requested =
        [
            "sa.wrong_side_of_the_tracks",
            "gta3.espresso_2_go"
        ];
        RunEngine engine = CreateEngine(catalog, out _, out _);
        RunSnapshot snapshot = engine.StartValidationRun(requested);
        Assert(snapshot.Mode == "validation", "Validation mode was not recorded.");
        Assert(
            snapshot.MissionOrder.SequenceEqual(requested),
            "The fixed validation plan was shuffled or changed.");

        bool duplicateRejected = false;
        try
        {
            RunEngine duplicateEngine = CreateEngine(catalog, out _, out _);
            duplicateEngine.StartValidationRun(
            [
                "sa.wrong_side_of_the_tracks",
                "sa.wrong_side_of_the_tracks"
            ]);
        }
        catch (ArgumentException)
        {
            duplicateRejected = true;
        }
        Assert(duplicateRejected, "A duplicate validation mission was accepted.");
    }

    private static void TestFiveGameValidationProgression(MissionCatalog catalog)
    {
        string[] route =
        [
            "sa.wrong_side_of_the_tracks",
            "gta3.espresso_2_go",
            "vc.demolition_man",
            "gta4.three_leaf_clover",
            "gtav.derailed"
        ];
        RunEngine engine = CreateEngine(
            catalog,
            out ManualClock clock,
            out _);
        RunSnapshot started = engine.StartValidationRun(route);

        for (int index = 0; index < route.Length; index++)
        {
            MissionDefinition mission = engine.CurrentMission
                ?? throw new InvalidOperationException(
                    $"Five-game route lost mission {index + 1}.");
            Assert(
                mission.Id == route[index],
                $"Five-game route changed mission {index + 1}.");
            string session = $"five-game-session-{index}";

            EngineResult ready = engine.AcceptBridgeEvent(Event(
                BridgeEventTypes.BridgeReady,
                "b1",
                mission,
                session,
                started.RunId));
            Assert(
                ready.Action == ControllerAction.PrepareMission,
                $"Five-game mission {index + 1} did not prepare.");

            EngineResult prepared = engine.AcceptBridgeEvent(Event(
                BridgeEventTypes.MissionPrepared,
                "b2",
                mission,
                session,
                started.RunId));
            Assert(
                prepared.Action == ControllerAction.StartMission,
                $"Five-game mission {index + 1} did not start.");

            engine.AcceptBridgeEvent(Event(
                BridgeEventTypes.PlayerControlGained,
                "b3",
                mission,
                session,
                started.RunId));
            clock.Advance(TimeSpan.FromSeconds(1));

            EngineResult completed = engine.AcceptBridgeEvent(Event(
                BridgeEventTypes.MissionCompleted,
                "b4",
                mission,
                session,
                started.RunId));
            ControllerAction expectedAction = index == route.Length - 1
                ? ControllerAction.FinishRun
                : ControllerAction.LaunchNextMission;
            Assert(
                completed.Action == expectedAction,
                $"Five-game mission {index + 1} produced {completed.Action}, " +
                $"expected {expectedAction}.");
        }

        RunSnapshot finished = engine.Snapshot;
        Assert(finished.Phase == RunPhase.Finished, "Five-game route did not finish.");
        Assert(
            finished.CompletedMissionIds.SequenceEqual(route),
            "Five-game completion order changed.");
        Assert(
            finished.GameplayElapsedMilliseconds == 5000,
            "Five-game gameplay timer did not span all five missions.");
    }

    private static void TestFiveMissionBetaPlans(MissionCatalog catalog)
    {
        string[] selected =
        [
            "sa.wrong_side_of_the_tracks",
            "gta3.espresso_2_go",
            "vc.demolition_man",
            "gta4.three_leaf_clover",
            "gtav.derailed"
        ];
        string[] expected = selected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        HashSet<string> trilogyGames = new(
            ["gta3de", "vcde", "sade"],
            StringComparer.Ordinal);

        for (uint seed = 1; seed <= 10_000; seed++)
        {
            RunEngine engine = CreateEngine(catalog, out _, out _);
            RunSnapshot plan = engine.StartBetaRun(selected, seed);
            Assert(plan.Mode == "normal-beta", "The Beta mode name was not recorded.");
            Assert(
                plan.MissionOrder.OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(expected),
                $"Beta seed {seed} changed the five-mission pool.");
            Assert(
                trilogyGames.Contains(catalog.GetMission(plan.MissionOrder[0]).Game),
                $"Beta seed {seed} did not start with a Trilogy mission.");

            RunEngine sameSeedEngine = CreateEngine(catalog, out _, out _);
            RunSnapshot sameSeedPlan = sameSeedEngine.StartBetaRun(selected, seed);
            Assert(
                plan.MissionOrder.SequenceEqual(sameSeedPlan.MissionOrder),
                $"Beta seed {seed} was not deterministic.");
        }
    }

    private static void TestPersistenceRedactsFutureOrder()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"ghmr-self-test-{Guid.NewGuid():N}");

        try
        {
            FileRunPersistence persistence = new(directory);
            RunSnapshot active = new()
            {
                RunId = "redaction-test",
                InternalSeed = 1234,
                Phase = RunPhase.Running,
                MissionOrder = ["mission.one", "mission.two", "mission.three"],
                CurrentIndex = 1
            };
            persistence.Save(active);
            RunSnapshot saved = persistence.Load()
                ?? throw new InvalidOperationException("Redacted state was not saved.");

            Assert(saved.InternalSeed == 0, "Internal seed leaked to active state.");
            Assert(saved.MissionOrder[0] == "mission.one", "Completed mission was lost.");
            Assert(saved.MissionOrder[1] == "mission.two", "Current mission was hidden.");
            Assert(saved.MissionOrder[2] == "hidden", "Future mission leaked to active state.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static RunEngine CreateEngine(
        MissionCatalog catalog,
        out ManualClock clock,
        out MemoryPersistence persistence)
    {
        clock = new ManualClock();
        persistence = new MemoryPersistence();
        return new RunEngine(catalog, persistence, new MemoryAudit(), clock);
    }

    private static BridgeEnvelope Event(
        string type,
        string messageId,
        MissionDefinition mission,
        string session,
        string runId)
    {
        return new BridgeEnvelope
        {
            Type = type,
            MessageId = messageId,
            Game = mission.Game,
            BridgeSessionId = session,
            RunId = runId,
            MissionId = mission.Id,
            BridgeVersion = "self-test",
            ExecutableVersion = "self-test"
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static GameLaunchProfile Profile(string game, string processName)
    {
        return new GameLaunchProfile
        {
            Game = game,
            LaunchExecutablePath = $"C:\\Games\\{processName}.exe",
            ProcessName = processName
        };
    }

    private sealed class ManualClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } =
            new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        public long Timestamp { get; private set; }

        public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp)
        {
            return TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        }

        public void Advance(TimeSpan value)
        {
            Timestamp += (long)value.TotalMilliseconds;
            UtcNow = UtcNow.Add(value);
        }
    }

    private sealed class MemoryPersistence : IRunPersistence
    {
        public List<RunSnapshot> Saves { get; } = [];

        public RunSnapshot? Load()
        {
            return Saves.LastOrDefault()?.Copy();
        }

        public void Save(RunSnapshot snapshot)
        {
            Saves.Add(snapshot.Copy());
        }
    }

    private sealed class MemoryAudit : IAuditSink
    {
        public void Append(
            string runId,
            long sequence,
            DateTimeOffset timestampUtc,
            string eventName,
            object? details = null)
        {
        }
    }

    private sealed class DictionaryProfileProvider : IGameLaunchProfileProvider
    {
        private readonly Dictionary<string, GameLaunchProfile> _profiles;

        public DictionaryProfileProvider(IEnumerable<GameLaunchProfile> profiles)
        {
            _profiles = profiles.ToDictionary(
                profile => profile.Game,
                StringComparer.Ordinal);
        }

        public GameLaunchProfile? Find(string game)
        {
            return _profiles.TryGetValue(game, out GameLaunchProfile? profile)
                ? profile
                : null;
        }
    }

    private sealed class RecordingProcessManager : ICompletedGameProcessManager
    {
        public List<string> Events { get; } = [];
        public GameProcessCloseOutcome CloseOutcome { get; init; } =
            GameProcessCloseOutcome.Closed;

        public Task LaunchAsync(
            GameLaunchProfile profile,
            CancellationToken cancellationToken = default)
        {
            Events.Add($"launch:{profile.Game}");
            return Task.CompletedTask;
        }

        public Task<GameProcessCloseResult> RequestGracefulCloseAsync(
            GameLaunchProfile profile,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Events.Add($"close:{profile.Game}:{(long)timeout.TotalMilliseconds}");
            return Task.FromResult(new GameProcessCloseResult(
                CloseOutcome,
                "self-test close result"));
        }

        public Task<GameProcessCloseResult> RequestCompletedGameCloseAsync(
            GameLaunchProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Events.Add($"completed-close:{profile.Game}:{(long)timeout.TotalMilliseconds}");
            return Task.FromResult(new GameProcessCloseResult(CloseOutcome, "self-test completed close result"));
        }
    }

    private sealed class CancelOnSettleDelay(CancellationTokenSource stop) : ITransitionDelay
    {
        private int _calls;
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (++_calls == 2) stop.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDelay : ITransitionDelay
    {
        private readonly List<string> _events;

        public RecordingDelay(List<string> events)
        {
            _events = events;
        }

        public Task WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            _events.Add($"delay:{(long)delay.TotalMilliseconds}");
            return Task.CompletedTask;
        }
    }
}
