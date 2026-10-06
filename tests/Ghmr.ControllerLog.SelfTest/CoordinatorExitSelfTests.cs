using Ghmr.Controller;
using Ghmr.Controller.Installation;
using Ghmr.Core;
using Ghmr.Core.Bridge;
using Ghmr.Core.Launch;
using Ghmr.Core.Run;

internal static class CoordinatorExitSelfTests
{
    public static async Task RunAsync(string dataDirectory)
    {
        MissionCatalog catalog = MissionCatalog.Load(
            Path.Combine(AppContext.BaseDirectory, "config", "missions.v0.1.json"));
        RunEngine engine = new(catalog, new FileRunPersistence(dataDirectory), new FileAuditSink(dataDirectory));
        IniBridgeServer bridge = new(dataDirectory);
        FakeProfiles profiles = new();
        FakeExitMonitor monitor = new();
        GameHandoffService handoff = new(profiles, new FakeProcesses());
        await using ControllerCoordinator coordinator = new(
            engine, bridge, handoff, profiles: profiles, processExitMonitor: monitor);
        TaskCompletionSource<bool> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool transitionHidden = false;
        int audioStops = 0;
        coordinator.TransitionChanged += (_, args) => transitionHidden = !args.Visible;
        coordinator.TransitionAudioStopRequested += (_, _) => audioStops++;
        coordinator.StatusChanged += (_, _) =>
        {
            if (coordinator.Status.Contains("closed. Run stopped", StringComparison.Ordinal))
                stopped.TrySetResult(true);
        };
        RunSnapshot run = engine.StartValidationRun(["gta3.espresso_2_go"]);
        BridgeEnvelope ready = new()
        {
            Type = BridgeEventTypes.BridgeReady,
            MessageId = "exit-ready",
            Game = "gta3de",
            BridgeSessionId = "exit-session",
            BridgeVersion = BridgeCompatibility.RequiredVersion("gta3de")
        };
        await bridge.MessageReceived!(ready);
        Require(monitor.WatchCount == 1, "An accepted bridge must arm an exit watch.");
        BridgeEnvelope prepared = ready with
        {
            Type = BridgeEventTypes.MissionPrepared,
            MessageId = "exit-prepared",
            RunId = run.RunId,
            MissionId = "gta3.espresso_2_go"
        };
        await bridge.MessageReceived!(prepared);
        await bridge.MessageReceived!(prepared with
        {
            Type = BridgeEventTypes.PlayerControlGained,
            MessageId = "exit-control"
        });
        Require(coordinator.Snapshot.Phase == RunPhase.Running, "Exit test must begin in a playable mission.");
        // Mission preparation already requests an audio stop. Count the exit's
        // additional request rather than treating earlier lifecycle work as an error.
        int audioStopsBeforeExit = audioStops;
        monitor.Exited.TrySetResult(true);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RunSnapshot ended = coordinator.Snapshot;
        Require(ended.Phase == RunPhase.Aborted && !ended.GameplayTimerRunning &&
            ended.ActiveBridgeSessionId is null, "Manual exit must stop the active run and clear its session.");
        Require(transitionHidden && audioStops == audioStopsBeforeExit + 1,
            "Manual exit must hide transition media and request one additional audio stop.");
        Require(ended.Failures == 0 && ended.CompletedMissionIds.Count == 0,
            "Manual exit must not count a mission failure or completion.");
        await Task.Delay(100);
        Require(coordinator.Snapshot.GameplayElapsedMilliseconds == ended.GameplayElapsedMilliseconds &&
            coordinator.Snapshot.RealElapsedMilliseconds == ended.RealElapsedMilliseconds,
            "Manual exit must freeze both timers.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task TestWindowMediaAsync(string dataDirectory)
    {
        MissionCatalog catalog = MissionCatalog.Load(
            Path.Combine(AppContext.BaseDirectory, "config", "missions.v0.1.json"));
        RunEngine engine = new(catalog, new FileRunPersistence(dataDirectory), new FileAuditSink(dataDirectory));
        IniBridgeServer bridge = new(dataDirectory);
        FakeProfiles profiles = new();
        PausedLaunch processes = new();
        GameHandoffService handoff = new(profiles, processes,
            fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
        await using ControllerCoordinator coordinator = new(engine, bridge, handoff);
        List<bool> media = [];
        coordinator.TransitionChanged += (_, state) => { lock (media) media.Add(state.Visible); };
        RunSnapshot run = engine.StartValidationRun(["gta4.three_leaf_clover", "gtav.derailed"]);
        BridgeEnvelope ready = new()
        {
            Type = BridgeEventTypes.BridgeReady, MessageId = "media-ready", Game = "gta4",
            BridgeSessionId = "iv-session", BridgeVersion = BridgeCompatibility.RequiredVersion("gta4")
        };
        await bridge.MessageReceived!(ready);
        BridgeEnvelope active = ready with
        {
            Type = BridgeEventTypes.MissionPrepared, MessageId = "media-prepared",
            RunId = run.RunId, MissionId = "gta4.three_leaf_clover"
        };
        await bridge.MessageReceived!(active);
        await bridge.MessageReceived!(active with { Type = BridgeEventTypes.PlayerControlGained, MessageId = "media-control" });
        await bridge.MessageReceived!(active with { Type = BridgeEventTypes.MissionCompleted, MessageId = "media-complete" });
        await processes.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(engine.Snapshot.CurrentIndex == 1, "GTA IV completion must select GTA V.");
        coordinator.DestinationWindowDetected("gta4", run.RunId, 0);
        lock (media) Require(media.Last(), "An outgoing game's window must not dismiss the new transition.");
        coordinator.DestinationWindowDetected("gtav_enhanced", run.RunId, 1);
        int afterDismiss;
        lock (media) { Require(!media.Last(), "The destination window must dismiss transition media."); afterDismiss = media.Count; }
        processes.Continue.TrySetResult(true);
        await Task.Delay(150);
        lock (media) Require(!media.Skip(afterDismiss).Any(value => value),
            "Late process discovery must never revive dismissed music or the transition screen.");
        Require(engine.Snapshot.CurrentIndex == 1 && engine.Snapshot.CompletedMissionIds.Count == 1,
            "Dismissing media must not alter mission results or advance the run.");
    }

    public static async Task TestCompletionRoutesAsync(string dataDirectory)
    {
        // Cover GTA V in both positions: it must hand off when another mission
        // follows, and finish the run when it is last. These are protocol tests;
        // the in-game adapter still has to produce a completion event.
        string[][] routes =
        [
            ["gtav.derailed", "gta3.espresso_2_go"],
            ["gta4.three_leaf_clover", "gtav.derailed"]
        ];
        for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            string directory = Path.Combine(dataDirectory, routeIndex.ToString());
            MissionCatalog catalog = MissionCatalog.Load(
                Path.Combine(AppContext.BaseDirectory, "config", "missions.v0.1.json"));
            RunEngine engine = new(catalog, new FileRunPersistence(directory), new FileAuditSink(directory));
            IniBridgeServer bridge = new(directory);
            RecordingProcesses processes = new();
            GameHandoffService handoff = new(new FakeProfiles(), processes,
                fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
            await using ControllerCoordinator coordinator = new(engine, bridge, handoff);
            RunSnapshot run = engine.StartValidationRun(routes[routeIndex]);
            for (int index = 0; index < routes[routeIndex].Length; index++)
            {
                MissionDefinition mission = coordinator.CurrentMission!;
                BridgeEnvelope ready = new()
                {
                    Type = BridgeEventTypes.BridgeReady, MessageId = $"ready-{index}",
                    Game = mission.Game, BridgeSessionId = $"session-{index}",
                    BridgeVersion = BridgeCompatibility.RequiredVersion(mission.Game)
                };
                await bridge.MessageReceived!(ready);
                BridgeEnvelope active = ready with
                {
                    Type = BridgeEventTypes.MissionPrepared, MessageId = $"prepared-{index}",
                    RunId = run.RunId, MissionId = mission.Id
                };
                await bridge.MessageReceived!(active);
                await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.PlayerControlGained, MessageId = $"control-{index}" });
                Require(coordinator.Snapshot.CompletedMissionIds.Count == index,
                    "Starting the next mission must not count it as completed.");
                await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.MissionCompleted, MessageId = $"complete-{index}" });
                Require(coordinator.Snapshot.CompletedMissionIds.Count == index + 1,
                    "Each accepted completion must count exactly once.");
                if (index == 0)
                {
                    string nextGame = coordinator.CurrentMission!.Game;
                    Require(await processes.Launched.Task.WaitAsync(TimeSpan.FromSeconds(5)) == nextGame,
                        "A non-final completion must launch the next selected game.");
                }
            }
            RunSnapshot finished = coordinator.Snapshot;
            Require(finished.Phase == RunPhase.Finished && !finished.GameplayTimerRunning,
                "The last confirmed completion must finish the run and stop its gameplay timer.");
            await Task.Delay(50);
            Require(coordinator.Snapshot.RealElapsedMilliseconds == finished.RealElapsedMilliseconds &&
                coordinator.Snapshot.GameplayElapsedMilliseconds == finished.GameplayElapsedMilliseconds,
                "Both timers must remain frozen after the run finishes.");
        }
    }

    public static async Task TestVisualPassAsync(string dataDirectory)
    {
        foreach (bool final in new[] { false, true })
        {
            string directory = Path.Combine(dataDirectory, final.ToString());
            MissionCatalog catalog = MissionCatalog.Load(
                Path.Combine(AppContext.BaseDirectory, "config", "missions.v0.1.json"));
            RunEngine engine = new(catalog, new FileRunPersistence(directory), new FileAuditSink(directory));
            IniBridgeServer bridge = new(directory);
            RecordingProcesses processes = new();
            GameHandoffService handoff = new(new FakeProfiles(), processes,
                fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
            await using ControllerCoordinator coordinator = new(engine, bridge, handoff);
            RunSnapshot run = engine.StartValidationRun(final
                ? ["gtav.derailed"] : ["gtav.derailed", "gta3.espresso_2_go"]);
            BridgeEnvelope ready = new()
            {
                Type = BridgeEventTypes.BridgeReady, MessageId = "visual-ready",
                Game = "gtav_enhanced", BridgeSessionId = "visual-session",
                BridgeVersion = BridgeCompatibility.RequiredVersion("gtav_enhanced")
            };
            await bridge.MessageReceived!(ready);
            BridgeEnvelope active = ready with
            {
                Type = BridgeEventTypes.MissionPrepared, MessageId = "visual-prepared",
                RunId = run.RunId, MissionId = "gtav.derailed"
            };
            await bridge.MessageReceived!(active);
            await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.PlayerControlGained, MessageId = "visual-control" });
            Require(!await coordinator.ConfirmGtaVPassScreenAsync("old-run", 0, "visual-session", 0) &&
                !await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 1, "visual-session", 0) &&
                !await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "old-session", 0) &&
                !await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "visual-session", 1),
                "Wrong run, index, bridge session and attempt must reject a screen result.");
            await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.MissionFailed, MessageId = "visual-failed", Reason = "death" });
            Require(!await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "visual-session", 1),
                "A retry that is not playable must reject a screen result.");
            await bridge.MessageReceived!(active with { MessageId = "visual-reprepared" });
            await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.PlayerControlGained, MessageId = "visual-recontrol" });
            Require(coordinator.Snapshot.Phase == RunPhase.Running &&
                !await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "visual-session", 0),
                "A result observed before the failure must not count on the new attempt.");
            Require(coordinator.Snapshot.CompletedMissionIds.Count == 0,
                "Rejected observations must never change the completed count.");
            Require(await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "visual-session", 1),
                "An active matching Derailed pass must enter the ordinary completion path.");
            Require(!await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "visual-session", 1),
                "The same pass screen cannot complete twice.");
            await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.MissionCompleted, MessageId = "late-native-pass" });
            Require(coordinator.Snapshot.CompletedMissionIds.Count == 1,
                "A late native completion after the screen confirmation cannot count twice.");
            if (final)
                Require(coordinator.Snapshot.Phase == RunPhase.Finished && !coordinator.Snapshot.GameplayTimerRunning,
                    "Derailed last must finish the run without waiting for replay cleanup.");
            else
            {
                Require(await processes.Launched.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "gta3de",
                    "Derailed before GTA III must launch GTA III after screen confirmation.");
                Require(coordinator.CurrentMission?.Id == "gta3.espresso_2_go" &&
                    coordinator.Snapshot.CurrentIndex == 1,
                    "A late Derailed event must not advance past the next selected game.");
            }
        }
    }

    public static async Task TestCompletedVManualExitAsync(string dataDirectory)
    {
        foreach (bool stop in new[] { false, true })
        {
            string directory = Path.Combine(dataDirectory, stop.ToString());
            MissionCatalog catalog = MissionCatalog.Load(
                Path.Combine(AppContext.BaseDirectory, "config", "missions.v0.1.json"));
            RunEngine engine = new(catalog, new FileRunPersistence(directory), new FileAuditSink(directory));
            IniBridgeServer bridge = new(directory);
            FakeProfiles profiles = new();
            ManualCloseProcesses processes = new();
            FakeExitMonitor monitor = new();
            GameHandoffService handoff = new(profiles, processes,
                fadeDelay: TimeSpan.Zero, launcherSettleDelay: TimeSpan.Zero);
            await using ControllerCoordinator coordinator = new(
                engine, bridge, handoff, profiles: profiles, processExitMonitor: monitor);
            RunSnapshot run = engine.StartValidationRun(["gtav.derailed", "gta3.espresso_2_go"]);
            BridgeEnvelope ready = new()
            {
                Type = BridgeEventTypes.BridgeReady, MessageId = "manual-ready", Game = "gtav_enhanced",
                BridgeSessionId = "manual-session", BridgeVersion = BridgeCompatibility.RequiredVersion("gtav_enhanced")
            };
            await bridge.MessageReceived!(ready);
            BridgeEnvelope active = ready with
            {
                Type = BridgeEventTypes.MissionPrepared, MessageId = "manual-prepared",
                MissionId = "gtav.derailed", RunId = run.RunId
            };
            await bridge.MessageReceived!(active);
            await bridge.MessageReceived!(active with
                { Type = BridgeEventTypes.PlayerControlGained, MessageId = "manual-control" });
            await coordinator.ConfirmGtaVPassScreenAsync(run.RunId, 0, "manual-session", 0);
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
            while (monitor.WatchCount < 2) await Task.Delay(10, deadline.Token);
            Require(processes.CloseCount == 1 && processes.LaunchCount == 0 &&
                coordinator.Snapshot.CompletedMissionIds.Count == 1,
                "A failed automatic close must preserve the pass and wait without launching GTA III.");
            if (stop) coordinator.AbortRun("manual recovery cancellation test");
            monitor.Exited.TrySetResult(true);
            if (!stop)
            {
                Require(await processes.Launched.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "gta3de",
                    "Closing completed GTA V manually must resume the selected GTA III launch.");
                // The old outgoing exit watch must also ignore this exit.
                await Task.Delay(600);
                Require(coordinator.Snapshot.Phase == RunPhase.Transitioning &&
                    coordinator.Snapshot.CompletedMissionIds.Count == 1 &&
                    processes.CloseCount == 1 && processes.LaunchCount == 1,
                    "Manual closure after a pass must not abort, count twice, re-close or duplicate the next launch.");
            }
            else
            {
                await Task.Delay(600);
                Require(coordinator.Snapshot.Phase == RunPhase.Aborted && processes.LaunchCount == 0,
                    "Stop Run must cancel manual-exit recovery before any next-game launch.");
            }
        }
    }

    private sealed class ManualCloseProcesses : ICompletedGameProcessManager
    {
        public int CloseCount { get; private set; }
        public int LaunchCount { get; private set; }
        public TaskCompletionSource<string> Launched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LaunchAsync(GameLaunchProfile profile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            Launched.TrySetResult(profile.Game);
            return Task.CompletedTask;
        }
        public Task<GameProcessCloseResult> RequestCompletedGameCloseAsync(
            GameLaunchProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            CloseCount++;
            return Task.FromResult(new GameProcessCloseResult(GameProcessCloseOutcome.ManualCloseRequired, "test close blocked"));
        }
        public Task<GameProcessCloseResult> RequestGracefulCloseAsync(
            GameLaunchProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A completed GTA V handoff must use the dedicated close capability.");
    }

    private sealed class RecordingProcesses : IGameProcessManager
    {
        public TaskCompletionSource<string> Launched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task LaunchAsync(GameLaunchProfile profile, CancellationToken cancellationToken = default)
        {
            Launched.TrySetResult(profile.Game);
            return Task.CompletedTask;
        }
        public Task<GameProcessCloseResult> RequestGracefulCloseAsync(
            GameLaunchProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(new GameProcessCloseResult(GameProcessCloseOutcome.AlreadyClosed, "test"));
    }

    private sealed class PausedLaunch : IGameProcessManager
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task LaunchAsync(GameLaunchProfile profile, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Continue.Task.WaitAsync(cancellationToken);
        }
        public Task<GameProcessCloseResult> RequestGracefulCloseAsync(
            GameLaunchProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(new GameProcessCloseResult(GameProcessCloseOutcome.AlreadyClosed, "test"));
    }

    private sealed class FakeProfiles : IGameLaunchProfileProvider
    {
        public GameLaunchProfile? Find(string game) => new()
        {
            Game = game,
            LaunchExecutablePath = "test-game.exe",
            ProcessName = "test-game"
        };
    }

    private sealed class FakeExitMonitor : IGameProcessExitMonitor
    {
        public TaskCompletionSource<bool> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WatchCount { get; private set; }
        public Task<bool> WaitForExitAsync(GameLaunchProfile profile, CancellationToken cancellationToken = default)
        {
            WatchCount++;
            return Exited.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FakeProcesses : IGameProcessManager
    {
        public Task LaunchAsync(GameLaunchProfile profile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<GameProcessCloseResult> RequestGracefulCloseAsync(
            GameLaunchProfile profile, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(new GameProcessCloseResult(GameProcessCloseOutcome.AlreadyClosed, "test"));
    }
}
