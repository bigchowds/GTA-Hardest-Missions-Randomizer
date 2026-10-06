using Ghmr.Core;
using Ghmr.Core.Bridge;
using Ghmr.Core.Launch;
using Ghmr.Core.Run;
using Ghmr.Controller.Diagnostics;
using Ghmr.Controller.Installation;
using System.Diagnostics;

namespace Ghmr.Controller;

public enum TransitionDisplayStage
{
    Closing,
    Launching,
    Connecting
}

public sealed class TransitionDisplayEventArgs : EventArgs
{
    public TransitionDisplayEventArgs(
        long sequence,
        bool visible,
        TransitionDisplayStage stage,
        string game,
        string mission,
        int missionNumber,
        int missionCount,
        string detail)
    {
        Sequence = sequence;
        Visible = visible;
        Stage = stage;
        Game = game;
        Mission = mission;
        MissionNumber = missionNumber;
        MissionCount = missionCount;
        Detail = detail;
    }

    public long Sequence { get; }
    public bool Visible { get; }
    public TransitionDisplayStage Stage { get; }
    public string Game { get; }
    public string Mission { get; }
    public int MissionNumber { get; }
    public int MissionCount { get; }
    public string Detail { get; }
}

public sealed class ControllerCoordinator : IAsyncDisposable
{
    private readonly RunEngine _engine;
    private readonly IniBridgeServer _bridgeServer;
    private readonly GameHandoffService _handoff;
    private readonly ControllerDiagnosticLog? _log;
    private readonly IGameLaunchProfileProvider? _profiles;
    private readonly IGameProcessExitMonitor? _processExitMonitor;
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private readonly List<Task> _gameExitWatches = [];
    private readonly System.Collections.Concurrent.ConcurrentBag<Task> _handoffTasks = [];
    private readonly object _handoffGate = new();
    private CancellationTokenSource? _handoffCancellation;
    private readonly CancellationTokenSource _shutdown = new();
    private long _commandSequence;
    private long _transitionSequence;
    private readonly object _presentationGate = new();
    private string? _mediaDismissedRun;
    private int _mediaDismissedIndex = -1;
    private string _status = "Controller ready.";

    public ControllerCoordinator(
        RunEngine engine,
        IniBridgeServer bridgeServer,
        GameHandoffService handoff,
        ControllerDiagnosticLog? log = null,
        IGameLaunchProfileProvider? profiles = null,
        IGameProcessExitMonitor? processExitMonitor = null)
    {
        _engine = engine;
        _bridgeServer = bridgeServer;
        _handoff = handoff;
        _log = log;
        _profiles = profiles;
        _processExitMonitor = processExitMonitor;
        _engine.StateChanged += (_, _) =>
        {
            LogRunState();
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        _bridgeServer.MessageReceived = HandleBridgeMessageAsync;
        _bridgeServer.ConnectionChanged += (_, args) =>
        {
            _log?.Info("Bridge", $"Transport connected={args.Connected}; game={args.Game}");
            MissionDefinition? current = _engine.CurrentMission;
            if (args.Game is not null &&
                !string.Equals(args.Game, current?.Game, StringComparison.Ordinal))
            {
                return;
            }

            string gameName = args.Game is null
                ? "Game"
                : DisplayGame(args.Game);
            SetStatus(args.Connected
                ? $"{gameName} bridge transport connected; verifying session."
                : $"{gameName} bridge transport disconnected.");
        };
        _bridgeServer.ProtocolError += (_, args) =>
        {
            _log?.Warning("Bridge", args.Message);
            SetStatus($"Bridge issue: {args.Message}");
        };
    }

    public event EventHandler? StateChanged;
    public event EventHandler? StatusChanged;
    public event EventHandler<TransitionDisplayEventArgs>? TransitionChanged;
    public event EventHandler? TransitionAudioStopRequested;

    public RunSnapshot Snapshot => _engine.Snapshot;
    public MissionDefinition? CurrentMission => _engine.CurrentMission;
    public int MissionCount => _engine.MissionCount;
    public string Status => _status;

    internal async Task<bool> ConfirmGtaVPassScreenAsync(
        string runId, int missionIndex, string bridgeSession, int failures)
    {
        await _eventGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            RunSnapshot state = _engine.Snapshot;
            if (state.Phase != RunPhase.Running || state.RunId != runId ||
                state.CurrentIndex != missionIndex || state.ActiveBridgeSessionId != bridgeSession ||
                state.Failures != failures || _engine.CurrentMission?.Id != "gtav.derailed")
                return false;
            _log?.Info("Completion", $"Derailed pass screen confirmed; source=foreground-banner-and-title; " +
                $"run={runId}; index={missionIndex}; failures={failures}");
            // Feed the same serialised completion path used by the adapter,
            // with an explicit evidence reason and a distinct event identity.
            // A late native event cannot double-count or advance another game.
            await HandleBridgeMessageCoreAsync(new BridgeEnvelope
            {
                Type = BridgeEventTypes.MissionCompleted,
                MessageId = $"screen-pass-{runId}-{missionIndex}-{failures}",
                Game = "gtav_enhanced", MissionId = "gtav.derailed", RunId = runId,
                BridgeSessionId = bridgeSession, Reason = "foreground-derailed-pass-screen"
            }).ConfigureAwait(false);
            return _engine.Snapshot.CompletedMissionIds.Contains("gtav.derailed");
        }
        finally { _eventGate.Release(); }
    }

    public void DestinationWindowDetected(string game, string runId, int missionIndex)
    {
        lock (_presentationGate)
        {
            RunSnapshot snapshot = _engine.Snapshot;
            if (snapshot.IsTerminal || snapshot.RunId != runId ||
                snapshot.CurrentIndex != missionIndex || _engine.CurrentMission?.Game != game ||
                (_mediaDismissedRun == runId && _mediaDismissedIndex == missionIndex))
                return;
            _mediaDismissedRun = runId;
            _mediaDismissedIndex = missionIndex;
            _log?.Info("Transition", $"Destination window detected; ending media; game={game}; index={missionIndex}");
            HideTransition();
        }
    }

    public void Start()
    {
        _bridgeServer.Start();
    }

    public async Task<RunSnapshot> BeginRunAsync(string mode = "development")
    {
        return await BeginStartedRunAsync(_engine.StartNewRun(mode))
            .ConfigureAwait(false);
    }

    public async Task<RunSnapshot> BeginValidationRunAsync(
        IReadOnlyList<string> missionIds)
    {
        return await BeginStartedRunAsync(_engine.StartValidationRun(missionIds))
            .ConfigureAwait(false);
    }

    public async Task<RunSnapshot> BeginBetaRunAsync(
        IReadOnlyList<string> missionIds)
    {
        return await BeginStartedRunAsync(_engine.StartBetaRun(missionIds))
            .ConfigureAwait(false);
    }

    private async Task<RunSnapshot> BeginStartedRunAsync(RunSnapshot snapshot)
    {
        CancelHandoff();
        _log?.Info("Run", $"Begin run={snapshot.RunId}; mode={snapshot.Mode}; " +
            $"missionCount={snapshot.MissionOrder.Count}; order={string.Join(",", snapshot.MissionOrder)}");
        _bridgeServer.Reset();
        // The first game is the beginning of the run, not a cross-game
        // handoff. Keep the controller's normal launch UI visible and reserve
        // the media transition for a completed mission that advances to a
        // different game.
        HideTransition();
        MissionDefinition mission = _engine.CurrentMission
            ?? throw new InvalidOperationException("The new run has no current mission.");
        _ = WarnIfBridgeMissingAsync(
            snapshot.RunId,
            mission.Game,
            snapshot.CurrentIndex);
        SetStatus($"Launching {DisplayGame(mission.Game)}.");

        try
        {
            GameHandoffResult launch = await _handoff.LaunchInitialAsync(
                    mission.Game,
                    _shutdown.Token)
                .ConfigureAwait(false);
            SetStatusIfWaitingForBridge(
                snapshot.RunId,
                mission.Game,
                snapshot.CurrentIndex,
                launch.Outcome == GameHandoffOutcome.Launched
                    ? $"{DisplayGame(mission.Game)} launched; waiting for its bridge."
                    : $"Configure {DisplayGame(mission.Game)}, or launch it manually, then wait for its bridge.");
        }
        catch (Exception exception) when (IsRecoverableLaunchFailure(exception))
        {
            _log?.Error("Launch", $"Initial launch failed; game={mission.Game}", exception);
            SetStatusIfWaitingForBridge(
                snapshot.RunId,
                mission.Game,
                snapshot.CurrentIndex,
                $"Automatic launch failed; launch {DisplayGame(mission.Game)} manually. {exception.Message}");
        }

        return snapshot;
    }

    private async Task WarnIfBridgeMissingAsync(
        string runId,
        string game,
        int currentIndex)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), _shutdown.Token).ConfigureAwait(false);
            SetStatusIfWaitingForBridge(
                runId,
                game,
                currentIndex,
                $"{DisplayGame(game)} bridge did not connect. If the game is waiting at its landing or save-selection screen, select Resume or the designated clean save once. If Rockstar's Connecting to Social Club dialog is visible, click OK once. Otherwise repair this game's bridge, then Abort Run.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    public RunSnapshot AbortRun(string reason)
    {
        _log?.Warning("Run", $"Abort requested; run={_engine.Snapshot.RunId}; reason={reason}");
        RunSnapshot snapshot = _engine.Abort(reason);
        CancelHandoff();
        HideTransition();
        SetStatus("Run aborted.");
        return snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        _log?.Info("Controller", "Stopping bridge polling and dispatch.");
        _shutdown.Cancel();
        await _bridgeServer.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(_gameExitWatches).ConfigureAwait(false);
        await Task.WhenAll(_handoffTasks).ConfigureAwait(false);
        _eventGate.Dispose();
        _shutdown.Dispose();
    }

    public static string DisplayGame(string game)
    {
        return game switch
        {
            "gta3de" => "GTA III: Definitive Edition",
            "vcde" => "GTA Vice City: Definitive Edition",
            "sade" => "GTA San Andreas: Definitive Edition",
            "gta4" => "GTA IV: Complete Edition",
            "gtav_enhanced" => "GTA V Enhanced",
            _ => game
        };
    }

    private async Task HandleBridgeMessageAsync(BridgeEnvelope message)
    {
        await _eventGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            await HandleBridgeMessageCoreAsync(message).ConfigureAwait(false);
        }
        finally
        {
            _eventGate.Release();
        }
    }

    private async Task WatchActiveGameExitAsync(
        RunSnapshot accepted,
        string game,
        string bridgeSessionId)
    {
        try
        {
            GameLaunchProfile? profile = _profiles?.Find(game);
            if (profile is null || _processExitMonitor is null) return;
            bool exited = await _processExitMonitor.WaitForExitAsync(profile, _shutdown.Token)
                .ConfigureAwait(false);
            if (!exited) return;
            // Let already-published completion events reach the dispatcher first.
            await Task.Delay(TimeSpan.FromMilliseconds(500), _shutdown.Token)
                .ConfigureAwait(false);
            await _eventGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            try
            {
                string reason = $"{DisplayGame(game)} closed before the run finished.";
                if (!_engine.AbortIfActiveGameExited(
                        accepted.RunId, accepted.CurrentIndex, game, bridgeSessionId, reason))
                {
                    _log?.Info("ProcessExit", $"Exit ignored after run/session advancement; " +
                        $"game={game}; run={accepted.RunId}; index={accepted.CurrentIndex}");
                    return;
                }
                _log?.Warning("ProcessExit", $"Active game exit stopped run; " +
                    $"game={game}; run={accepted.RunId}; index={accepted.CurrentIndex}");
                HideTransition();
                TransitionAudioStopRequested?.Invoke(this, EventArgs.Empty);
                SetStatus($"{DisplayGame(game)} closed. Run stopped; start a new run when ready.");
            }
            finally
            {
                _eventGate.Release();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // An inaccessible process or monitoring error is not proof of exit.
            _log?.Error("ProcessExit", $"Exit watch failed; game={game}; " +
                $"run={accepted.RunId}; active run retained", exception);
        }
    }

    private async Task HandleBridgeMessageCoreAsync(BridgeEnvelope message)
    {
        bool logEvent = message.Type != BridgeEventTypes.Heartbeat;
        if (logEvent)
            _log?.Info("Bridge", $"Dispatch event={message.Type}; game={message.Game}; " +
                $"message={message.MessageId}; run={message.RunId}; mission={message.MissionId}; " +
                $"session={message.BridgeSessionId}; bridgeVersion={message.BridgeVersion}; " +
                $"executableVersion={message.ExecutableVersion}");

        RunSnapshot snapshot = _engine.Snapshot;
        if (message.Type == BridgeEventTypes.BridgeReady &&
            snapshot.Phase != RunPhase.Idle &&
            !snapshot.IsTerminal &&
            string.Equals(_engine.CurrentMission?.Game, message.Game, StringComparison.Ordinal) &&
            !BridgeCompatibility.IsReportedVersionCompatible(
                message.Game,
                message.BridgeVersion,
                out string requiredBridgeVersion))
        {
            string reportedBridgeVersion = string.IsNullOrWhiteSpace(message.BridgeVersion)
                ? "missing"
                : message.BridgeVersion;
            string reason =
                $"{DisplayGame(message.Game)} reported bridge v{reportedBridgeVersion}; " +
                $"this controller requires v{requiredBridgeVersion}.";
            _log?.Warning("Bridge", $"Outdated bridge rejected before mission start; " +
                $"game={message.Game}; reported={reportedBridgeVersion}; " +
                $"required={requiredBridgeVersion}; run={snapshot.RunId}");
            _engine.Abort(reason);
            HideTransition();
            SetStatus(reason + " Open Setup Game Bridges and install/repair it.");
            return;
        }

        EngineResult result = _engine.AcceptBridgeEvent(message);
        if (logEvent)
            _log?.Info("Bridge", $"Event decision={result.Disposition}; action={result.Action}; " +
                $"event={message.Type}; game={message.Game}; reason={result.Reason}");
        if (result.Disposition == MessageDisposition.Rejected)
        {
            SetStatus($"Bridge event rejected: {result.Reason}");
            return;
        }

        if (result.Disposition == MessageDisposition.IgnoredDuplicate)
        {
            return;
        }

        if (result.Action == ControllerAction.PrepareMission &&
            _engine.Snapshot.ActiveBridgeSessionId is string activeSession &&
            _engine.CurrentMission is MissionDefinition activeMission)
        {
            _gameExitWatches.Add(WatchActiveGameExitAsync(
                _engine.Snapshot, activeMission.Game, activeSession));
        }

        if (message.Type == BridgeEventTypes.PlayerControlGained)
        {
            HideTransition();
        }

        switch (result.Action)
        {
            case ControllerAction.PrepareMission:
                await SendCurrentMissionCommandAsync(ControllerCommandTypes.PrepareMission)
                    .ConfigureAwait(false);
                SetStatus("Preparing the locked mission.");
                break;

            case ControllerAction.StartMission:
                // Request an asynchronous UI-thread audio stop. Never wait on
                // multimedia playback from the serialized bridge dispatcher:
                // mission launch and later lifecycle events must keep flowing.
                TransitionAudioStopRequested?.Invoke(this, EventArgs.Empty);
                await SendCurrentMissionCommandAsync(ControllerCommandTypes.StartMission)
                    .ConfigureAwait(false);
                SetStatus(message.Game == "gtav_enhanced"
                    ? "GTA V is ready. Derailed was requested automatically; waiting for Rockstar's replay controller."
                    : "Starting mission; waiting for player control.");
                break;

            case ControllerAction.RestartMission:
                await SendCurrentMissionCommandAsync(ControllerCommandTypes.RestartMission)
                    .ConfigureAwait(false);
                SetStatus(message.Game == "gtav_enhanced"
                    ? "Failure recorded. Use GTA V's native Retry option; the bridge will reattach without forcing mission cleanup."
                    : "Failure recorded; restarting the same mission.");
                break;

            case ControllerAction.LaunchNextMission:
                MissionDefinition next = _engine.CurrentMission
                    ?? throw new InvalidOperationException("Next mission is missing.");
                RunSnapshot transition = _engine.Snapshot;
                PublishTransition(
                    transition,
                    next,
                    TransitionDisplayStage.Closing,
                    $"Closing {DisplayGame(message.Game)} safely.");
                await TryReleaseBridgeAsync(message, fadeRequested: true)
                    .ConfigureAwait(false);
                // Rotate the next game's INI generation before its process is
                // launched. This also forces an already-running/reloaded CLEO
                // adapter to send a fresh bridgeReady event.
                _bridgeServer.PrepareForLaunch(next.Game);
                SetStatus(
                    $"Mission complete. Closing {DisplayGame(message.Game)} and preparing the platform handoff to {DisplayGame(next.Game)}.");
                // Do not await launcher/process discovery on the one serialized
                // bridge dispatcher. GTA III can reach free roam and send
                // bridgeReady while Rockstar/Steam process discovery is still
                // settling; that event must be accepted immediately.
                _handoffTasks.Add(PerformHandoffAsync(
                    message.Game,
                    next.Game,
                    transition.RunId,
                    transition.CurrentIndex));
                break;

            case ControllerAction.FinishRun:
                HideTransition();
                await TryReleaseBridgeAsync(message, fadeRequested: false)
                    .ConfigureAwait(false);
                SetStatus(
                    $"All {_engine.Snapshot.MissionOrder.Count} missions complete. " +
                    $"{DisplayGame(message.Game)} remains open; close it normally when ready.");
                break;

            case ControllerAction.AbortRun:
                HideTransition();
                SetStatus(
                    $"Bridge error ({message.Reason ?? "unknown"}) invalidated the run.");
                break;

            case ControllerAction.None:
            default:
                SetStatus(result.Reason);
                break;
        }
    }

    private async Task SendCurrentMissionCommandAsync(string type)
    {
        RunSnapshot snapshot = _engine.Snapshot;
        MissionDefinition mission = _engine.CurrentMission
            ?? throw new InvalidOperationException("No current mission is available.");
        string sessionId = snapshot.ActiveBridgeSessionId
            ?? throw new InvalidOperationException("No accepted bridge session is active.");

        BridgeEnvelope command = new()
        {
            Type = type,
            MessageId = $"controller-{Interlocked.Increment(ref _commandSequence)}",
            Game = mission.Game,
            BridgeSessionId = sessionId,
            RunId = snapshot.RunId,
            MissionId = mission.Id
        };

        try
        {
            await _bridgeServer.SendAsync(command).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or InvalidDataException)
        {
            _log?.Error("Bridge", $"Sending command failed; type={type}; game={mission.Game}", exception);
            _engine.Abort("Lost the active game bridge while sending a command.");
            SetStatus("Bridge connection was lost; run invalidated.");
        }
    }

    private async Task TryReleaseBridgeAsync(
        BridgeEnvelope completedEvent,
        bool fadeRequested)
    {
        _log?.Info("Handoff", $"Release bridge requested; game={completedEvent.Game}; " +
            $"run={completedEvent.RunId}; mission={completedEvent.MissionId}; fade={fadeRequested}");
        BridgeEnvelope command = new()
        {
            Type = ControllerCommandTypes.ReleaseBridge,
            MessageId = $"controller-{Interlocked.Increment(ref _commandSequence)}",
            Game = completedEvent.Game,
            BridgeSessionId = completedEvent.BridgeSessionId,
            RunId = completedEvent.RunId,
            MissionId = completedEvent.MissionId,
            Reason = fadeRequested ? null : "done"
        };

        try
        {
            bool acknowledged = await _bridgeServer.SendAndWaitForAckAsync(
                    command, TimeSpan.FromSeconds(2), _shutdown.Token)
                .ConfigureAwait(false);
            _log?.Info("Handoff", $"Release acknowledgement={acknowledged}; game={completedEvent.Game}");
            if (acknowledged && fadeRequested)
            {
                // CLEO acknowledges before applying the release command. Give
                // its requested 500 ms fade time to finish before closing.
                await Task.Delay(TimeSpan.FromMilliseconds(600), _shutdown.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _log?.Error("Handoff", $"Release handshake failed; continuing handoff; " +
                $"game={completedEvent.Game}", exception);
            // A bridge may disconnect immediately after reporting completion.
            // The accepted completion remains valid and the next game may connect.
        }
    }

    private async Task PerformHandoffAsync(
        string completedGame,
        string nextGame,
        string runId,
        int currentIndex)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        lock (_handoffGate)
        {
            _handoffCancellation?.Cancel();
            _handoffCancellation = cancellation;
        }
        bool StillCurrent()
        {
            RunSnapshot state = _engine.Snapshot;
            return !state.IsTerminal && state.RunId == runId &&
                state.CurrentIndex == currentIndex && _engine.CurrentMission?.Game == nextGame;
        }
        Stopwatch timer = Stopwatch.StartNew();
        _log?.Info("Handoff", $"Switch started; run={runId}; from={completedGame}; " +
            $"to={nextGame}; missionNumber={currentIndex + 1}");
        try
        {
            // Do not hold the serialized bridge dispatcher during closure.
            await Task.Yield();
            cancellation.Token.ThrowIfCancellationRequested();
            if (!StillCurrent()) return;
            GameHandoffResult result = await _handoff.TransitionAsync(
                    completedGame,
                    nextGame,
                    cancellation.Token,
                    progress => ReportHandoffProgress(
                        progress,
                        runId,
                        currentIndex))
                .ConfigureAwait(false);
            if (completedGame == "gtav_enhanced" && StillCurrent() &&
                _engine.Snapshot.Phase == RunPhase.Transitioning &&
                (result.Outcome is GameHandoffOutcome.ManualCloseRequired or GameHandoffOutcome.CloseTimedOut) &&
                _profiles?.Find(completedGame) is GameLaunchProfile completedProfile &&
                _processExitMonitor is not null)
            {
                // The pass is already recorded. A later Task Manager/manual
                // close ends this pending handoff instead of stranding it.
                HideTransition();
                SetStatusIfWaitingForBridge(runId, nextGame, currentIndex,
                    $"Close {DisplayGame(completedGame)} manually; GHMR will launch {DisplayGame(nextGame)} automatically after it exits.");
                _log?.Info("Handoff", $"Waiting for completed GTA V manual exit; run={runId}; index={currentIndex}; closeResult={result.Outcome}");
                bool exited = await _processExitMonitor.WaitForExitAsync(completedProfile, cancellation.Token)
                    .ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!StillCurrent() || _engine.Snapshot.Phase != RunPhase.Transitioning) return;
                if (exited)
                {
                    _log?.Info("Handoff", "Completed GTA V manual exit observed; resuming the selected next-game launch.");
                    result = await _handoff.ResumeAfterCompletedGameExitAsync(
                        completedGame, nextGame, cancellation.Token,
                        progress => ReportHandoffProgress(progress, runId, currentIndex)).ConfigureAwait(false);
                }
            }
            _log?.Info("Handoff", $"Switch result={result.Outcome}; from={completedGame}; " +
                $"to={nextGame}; elapsedMs={timer.ElapsedMilliseconds}; detail={result.Detail}");
            if (result.Outcome != GameHandoffOutcome.Launched)
            {
                if (StillCurrent()) HideTransition();
            }
            else
                _ = WarnIfBridgeMissingAfterHandoffAsync(runId, nextGame, currentIndex);
            SetStatusIfWaitingForBridge(
                runId,
                nextGame,
                currentIndex,
                result.Outcome switch
            {
                GameHandoffOutcome.Launched =>
                    $"{DisplayGame(nextGame)} launched; waiting for its bridge.",
                GameHandoffOutcome.ProfileMissing =>
                    $"Automatic switching is not configured. Close {DisplayGame(completedGame)} normally, then launch {DisplayGame(nextGame)} manually.",
                GameHandoffOutcome.ManualCloseRequired =>
                    $"Close {DisplayGame(completedGame)} normally, then launch {DisplayGame(nextGame)} manually.",
                GameHandoffOutcome.CloseTimedOut =>
                    $"{DisplayGame(completedGame)} is still open. Close it normally, then launch {DisplayGame(nextGame)} manually.",
                _ => result.Detail
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _log?.Info("Handoff", $"Switch cancelled after Stop Run, a new run or controller shutdown; from={completedGame}; to={nextGame}");
        }
        catch (Exception exception) when (IsRecoverableLaunchFailure(exception))
        {
            _log?.Error("Handoff", $"Switch stopped; from={completedGame}; to={nextGame}; " +
                $"elapsedMs={timer.ElapsedMilliseconds}", exception);
            if (StillCurrent()) HideTransition();
            SetStatusIfWaitingForBridge(
                runId,
                nextGame,
                currentIndex,
                $"Automatic switching stopped safely. Close {DisplayGame(completedGame)} normally and launch {DisplayGame(nextGame)} manually. {exception.Message}");
        }
        catch (Exception exception)
        {
            _log?.Error("Handoff", $"Switch failed unexpectedly; from={completedGame}; to={nextGame}; " +
                $"elapsedMs={timer.ElapsedMilliseconds}", exception);
            // This task deliberately runs outside the serialized bridge-event
            // dispatcher. Observe every fault so a launcher failure can never
            // become an unobserved background exception.
            if (StillCurrent()) HideTransition();
            SetStatusIfWaitingForBridge(
                runId,
                nextGame,
                currentIndex,
                $"Automatic switching failed. Launch {DisplayGame(nextGame)} manually. {exception.Message}");
        }
        finally
        {
            lock (_handoffGate)
                if (ReferenceEquals(_handoffCancellation, cancellation))
                    _handoffCancellation = null;
        }
    }

    private void CancelHandoff()
    {
        lock (_handoffGate) _handoffCancellation?.Cancel();
    }

    private async Task WarnIfBridgeMissingAfterHandoffAsync(
        string runId,
        string game,
        int currentIndex)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), _shutdown.Token)
                .ConfigureAwait(false);
            SetStatusIfWaitingForBridge(
                runId,
                game,
                currentIndex,
                $"{DisplayGame(game)} is running but its bridge has not connected. If the game is waiting at its landing or save-selection screen, select Resume or the designated clean save once. If Rockstar's Connecting to Social Club dialog is visible, click OK once. Otherwise use Setup Games & Bridges to repair this game.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private void SetStatusIfWaitingForBridge(
        string runId,
        string game,
        int currentIndex,
        string status)
    {
        RunSnapshot snapshot = _engine.Snapshot;
        MissionDefinition? mission = _engine.CurrentMission;
        if (snapshot.RunId == runId &&
            snapshot.CurrentIndex == currentIndex &&
            snapshot.ActiveBridgeSessionId is null &&
            !snapshot.IsTerminal &&
            string.Equals(mission?.Game, game, StringComparison.Ordinal))
        {
            SetStatus(status);
        }
    }

    private void ReportHandoffProgress(
        GameHandoffProgress progress,
        string runId,
        int currentIndex)
    {
        _log?.Info("Handoff", $"Stage={progress.Stage}; run={runId}; " +
            $"from={progress.CompletedGame}; to={progress.NextGame}");
        (TransitionDisplayStage stage, string detail) = progress.Stage switch
        {
            GameHandoffStage.ClosingCompletedGame => (
                TransitionDisplayStage.Closing,
                $"Closing {DisplayGame(progress.CompletedGame)} safely."),
            GameHandoffStage.SettlingPlatform => (
                TransitionDisplayStage.Closing,
                "Allowing Steam and Rockstar services to settle."),
            GameHandoffStage.LaunchingNextGame => (
                TransitionDisplayStage.Launching,
                $"Starting {DisplayGame(progress.NextGame)}."),
            GameHandoffStage.NextGameDetected => (
                TransitionDisplayStage.Connecting,
                "Game detected. Connecting its GHMR bridge."),
            _ => throw new InvalidOperationException("Unknown handoff stage.")
        };

        PublishTransitionIfWaitingForBridge(
            runId,
            progress.NextGame,
            currentIndex,
            stage,
            detail);
    }

    private void PublishTransitionIfWaitingForBridge(
        string runId,
        string game,
        int currentIndex,
        TransitionDisplayStage stage,
        string detail)
    {
        RunSnapshot snapshot = _engine.Snapshot;
        MissionDefinition? mission = _engine.CurrentMission;
        if (mission is not null &&
            snapshot.RunId == runId &&
            snapshot.CurrentIndex == currentIndex &&
            snapshot.ActiveBridgeSessionId is null &&
            !snapshot.IsTerminal &&
            string.Equals(mission.Game, game, StringComparison.Ordinal))
        {
            PublishTransition(snapshot, mission, stage, detail);
        }
    }

    private void PublishTransition(
        RunSnapshot snapshot,
        MissionDefinition mission,
        TransitionDisplayStage stage,
        string detail)
    {
        lock (_presentationGate)
        {
            // Launcher discovery can finish after the window has already appeared.
            // Its late progress must not bring dismissed media back or restart music.
            if (_mediaDismissedRun == snapshot.RunId && _mediaDismissedIndex == snapshot.CurrentIndex)
                return;
            TransitionChanged?.Invoke(
                this,
                new TransitionDisplayEventArgs(
                    Interlocked.Increment(ref _transitionSequence),
                    true,
                    stage,
                    DisplayGame(mission.Game),
                    mission.Title,
                    Math.Min(snapshot.CurrentIndex + 1, snapshot.MissionOrder.Count),
                    snapshot.MissionOrder.Count,
                    detail));
        }
    }

    private void HideTransition()
    {
        TransitionChanged?.Invoke(
            this,
            new TransitionDisplayEventArgs(
                Interlocked.Increment(ref _transitionSequence),
                false,
                TransitionDisplayStage.Connecting,
                string.Empty,
                string.Empty,
                0,
                0,
                string.Empty));
    }

    private static bool IsRecoverableLaunchFailure(Exception exception)
    {
        return exception is IOException or InvalidOperationException or
            InvalidDataException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;
    }

    private void SetStatus(string status)
    {
        if (!string.Equals(_status, status, StringComparison.Ordinal))
            _log?.Info("Status", status);
        _status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LogRunState()
    {
        if (_log is null) return;
        RunSnapshot state = _engine.Snapshot;
        _log.Info("Run", $"State run={state.RunId}; phase={state.Phase}; " +
            $"game={state.ActiveGame}; mission={_engine.CurrentMission?.Id}; " +
            $"index={state.CurrentIndex}; completed={state.CompletedMissionIds.Count}; " +
            $"failures={state.Failures}; bridgeSession={state.ActiveBridgeSessionId}; " +
            $"gameplayMs={state.GameplayElapsedMilliseconds}; elapsedMs={state.RealElapsedMilliseconds}");
    }
}
