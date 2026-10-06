using System.Security.Cryptography;
using System.Text;
using Ghmr.Core.Bridge;

namespace Ghmr.Core.Run;

public sealed class RunEngine
{
    private static readonly HashSet<string> TrilogyGames = new(
        ["gta3de", "vcde", "sade"],
        StringComparer.Ordinal);

    private readonly MissionCatalog _catalog;
    private readonly IRunPersistence _persistence;
    private readonly IAuditSink _audit;
    private readonly IClock _clock;
    private readonly object _sync = new();
    private readonly HashSet<string> _acceptedMessageIds = new(StringComparer.Ordinal);

    private RunSnapshot _state = new();
    private long? _runStartedTimestamp;
    private long? _gameplayStartedTimestamp;

    public RunEngine(
        MissionCatalog catalog,
        IRunPersistence persistence,
        IAuditSink audit,
        IClock? clock = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? new SystemClock();
    }

    public event EventHandler? StateChanged;

    public RunSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return CreateLiveSnapshot();
            }
        }
    }

    public MissionDefinition? CurrentMission
    {
        get
        {
            lock (_sync)
            {
                return GetCurrentMission();
            }
        }
    }

    public int MissionCount => _catalog.Missions.Count;

    public RunSnapshot StartNewRun(string mode = "development", uint? seed = null)
    {
        lock (_sync)
        {
            EnsureNoActiveRun();

            if (!string.Equals(mode, "normal", StringComparison.Ordinal) &&
                !string.Equals(mode, "chaos", StringComparison.Ordinal) &&
                !string.Equals(mode, "development", StringComparison.Ordinal))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    "Mode must be normal, chaos or development.");
            }

            if (string.Equals(mode, "chaos", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Chaos mode is reserved for a later milestone after Normal-mode validation.");
            }

            if (!string.Equals(mode, "development", StringComparison.Ordinal) &&
                _catalog.Missions.Any(mission =>
                    !string.Equals(mission.BridgeStatus, "verified", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Normal mode remains locked until every selected bridge is verified.");
            }

            uint internalSeed = seed ?? CreateSeed();
            List<string> missionOrder = ShuffleMissionIds(
                _catalog.Missions.Select(mission => mission.Id),
                internalSeed);
            return StartLockedRun(mode, internalSeed, missionOrder);
        }
    }

    public RunSnapshot StartBetaRun(
        IReadOnlyList<string> missionIds,
        uint? seed = null)
    {
        ArgumentNullException.ThrowIfNull(missionIds);

        lock (_sync)
        {
            EnsureNoActiveRun();
            List<string> selectedMissionIds = ValidateMissionIds(
                missionIds,
                "A Beta run");
            uint internalSeed = seed ?? CreateSeed();
            List<string> missionOrder = ShuffleMissionIds(
                selectedMissionIds,
                internalSeed);
            return StartLockedRun("normal-beta", internalSeed, missionOrder);
        }
    }

    public RunSnapshot StartValidationRun(IReadOnlyList<string> missionIds)
    {
        ArgumentNullException.ThrowIfNull(missionIds);

        lock (_sync)
        {
            EnsureNoActiveRun();
            List<string> missionOrder = ValidateMissionIds(
                missionIds,
                "A validation run");

            return StartLockedRun("validation", internalSeed: 0, missionOrder);
        }
    }

    public EngineResult AcceptBridgeEvent(BridgeEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            EngineResult? validationResult = ValidateEnvelope(message);
            if (validationResult is not null)
            {
                return validationResult;
            }

            string dedupeKey = MessageDedupeKey(message);
            if (!_acceptedMessageIds.Add(dedupeKey))
            {
                return new EngineResult(
                    MessageDisposition.IgnoredDuplicate,
                    ControllerAction.None,
                    "Duplicate message id.");
            }

            if (message.Type == BridgeEventTypes.Heartbeat)
            {
                return new EngineResult(
                    MessageDisposition.Accepted,
                    ControllerAction.None,
                    "Heartbeat accepted.");
            }

            _state.AcceptedMessageIds.Add(dedupeKey);

            return message.Type switch
            {
                BridgeEventTypes.BridgeReady => HandleBridgeReady(message),
                BridgeEventTypes.GameReady => HandleGameReady(),
                BridgeEventTypes.MissionPrepared => HandleMissionPrepared(message),
                BridgeEventTypes.PlayerControlGained => HandlePlayerControlGained(message),
                BridgeEventTypes.PlayerControlLost => HandlePlayerControlLost(message),
                BridgeEventTypes.MissionFailed => HandleMissionFailed(message),
                BridgeEventTypes.MissionCompleted => HandleMissionCompleted(message),
                BridgeEventTypes.BridgeError => HandleBridgeError(message),
                _ => RejectAfterDedupe(message, "Unknown bridge event type.")
            };
        }
    }

    public bool AbortIfActiveGameExited(
        string runId,
        int currentIndex,
        string game,
        string bridgeSessionId,
        string reason)
    {
        lock (_sync)
        {
            // An old game's exit is normal during a handoff. A late watcher
            // must also never stop a new run, a new session or a finished run.
            if (_state.RunId != runId || _state.CurrentIndex != currentIndex ||
                _state.ActiveGame != game ||
                _state.ActiveBridgeSessionId != bridgeSessionId ||
                _state.Phase is not (RunPhase.Preparing or RunPhase.Running or RunPhase.Restarting))
                return false;

            _state.ActiveBridgeSessionId = null;
            Abort(reason);
            return true;
        }
    }

    public RunSnapshot Abort(string reason)
    {
        lock (_sync)
        {
            if (_state.IsTerminal || _state.Phase == RunPhase.Idle)
            {
                return CreateLiveSnapshot();
            }

            StopGameplayTimer();
            _state.Phase = RunPhase.Aborted;
            _state.EndedUtc = _clock.UtcNow;
            StopRunTimer();
            Commit("runAborted", new { reason });
            return CreateLiveSnapshot();
        }
    }

    public static RunSnapshot MarkInterrupted(
        RunSnapshot snapshot,
        IRunPersistence persistence,
        IAuditSink audit,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(audit);

        if (snapshot.IsTerminal || snapshot.Phase == RunPhase.Idle)
        {
            return snapshot;
        }

        IClock effectiveClock = clock ?? new SystemClock();
        snapshot.Phase = RunPhase.Interrupted;
        snapshot.GameplayTimerRunning = false;
        snapshot.EndedUtc = effectiveClock.UtcNow;
        snapshot.LastEventUtc = effectiveClock.UtcNow;
        snapshot.AuditSequence++;
        audit.Append(
            snapshot.RunId,
            snapshot.AuditSequence,
            snapshot.LastEventUtc,
            "runInterrupted",
            new { reason = "Controller restarted while a run was active." });
        persistence.Save(snapshot);
        return snapshot;
    }

    private EngineResult? ValidateEnvelope(BridgeEnvelope message)
    {
        if (_state.Phase == RunPhase.Idle || _state.IsTerminal)
        {
            return Reject("No active run.");
        }

        if (message.Protocol != BridgeProtocol.Version)
        {
            return Reject($"Unsupported protocol version: {message.Protocol}");
        }

        if (string.IsNullOrWhiteSpace(message.Type) ||
            string.IsNullOrWhiteSpace(message.MessageId) ||
            string.IsNullOrWhiteSpace(message.Game) ||
            string.IsNullOrWhiteSpace(message.BridgeSessionId))
        {
            return Reject("Required bridge fields are missing.");
        }

        MissionDefinition? mission = GetCurrentMission();
        if (mission is null || !string.Equals(message.Game, mission.Game, StringComparison.Ordinal))
        {
            return Reject("Event came from a game that is not current.");
        }

        if (message.RunId is not null &&
            !string.Equals(message.RunId, _state.RunId, StringComparison.Ordinal))
        {
            return Reject("Event belongs to a stale run.");
        }

        if (message.Type != BridgeEventTypes.BridgeReady &&
            !string.Equals(
                message.BridgeSessionId,
                _state.ActiveBridgeSessionId,
                StringComparison.Ordinal))
        {
            return Reject("Event belongs to a stale bridge session.");
        }

        if (message.Type == BridgeEventTypes.BridgeReady &&
            _state.ActiveBridgeSessionId is not null &&
            _state.Phase is RunPhase.Running or RunPhase.Restarting)
        {
            return Reject("A different bridge cannot replace the active mission session.");
        }

        return null;
    }

    private EngineResult HandleBridgeReady(BridgeEnvelope message)
    {
        _state.ActiveBridgeSessionId = message.BridgeSessionId;
        _state.ActiveGame = message.Game;
        _state.Phase = RunPhase.Preparing;
        Commit(
            BridgeEventTypes.BridgeReady,
            new
            {
                message.Game,
                message.BridgeVersion,
                message.ExecutableVersion,
                message.BridgeSessionId
            });
        return Accepted(ControllerAction.PrepareMission, "Bridge accepted.");
    }

    private EngineResult HandleGameReady()
    {
        _state.Phase = RunPhase.Preparing;
        Commit(BridgeEventTypes.GameReady);
        return Accepted(ControllerAction.None, "Game is ready.");
    }

    private EngineResult HandleMissionPrepared(BridgeEnvelope message)
    {
        if (!MessageTargetsCurrentMission(message))
        {
            return RejectAfterDedupe(message, "Prepared mission does not match the locked mission.");
        }

        _state.Phase = RunPhase.Preparing;
        Commit(BridgeEventTypes.MissionPrepared, new { message.MissionId });
        return Accepted(ControllerAction.StartMission, "Mission prepared.");
    }

    private EngineResult HandlePlayerControlGained(BridgeEnvelope message)
    {
        if (!MessageTargetsCurrentMission(message))
        {
            return RejectAfterDedupe(message, "Control event does not match the locked mission.");
        }

        if (_state.Phase is not (RunPhase.Preparing or RunPhase.Restarting or RunPhase.Running))
        {
            return RejectAfterDedupe(message, "Player control was reported in an invalid run phase.");
        }

        _state.Phase = RunPhase.Running;
        StartGameplayTimer();
        Commit(BridgeEventTypes.PlayerControlGained, new { message.MissionId });
        return Accepted(ControllerAction.None, "Gameplay timer running.");
    }

    private EngineResult HandlePlayerControlLost(BridgeEnvelope message)
    {
        if (!MessageTargetsCurrentMission(message))
        {
            return RejectAfterDedupe(message, "Control event does not match the locked mission.");
        }

        if (_state.Phase != RunPhase.Running)
        {
            return RejectAfterDedupe(message, "Player-control loss was reported outside gameplay.");
        }

        StopGameplayTimer();
        Commit(BridgeEventTypes.PlayerControlLost, new { message.MissionId, message.Reason });
        return Accepted(ControllerAction.None, "Gameplay timer paused.");
    }

    private EngineResult HandleMissionFailed(BridgeEnvelope message)
    {
        if (!MessageTargetsCurrentMission(message))
        {
            return RejectAfterDedupe(message, "Failure does not match the locked mission.");
        }

        if (_state.Phase != RunPhase.Running)
        {
            return RejectAfterDedupe(message, "Failure was reported outside the running mission.");
        }

        StopGameplayTimer();
        _state.Failures++;
        _state.Phase = RunPhase.Restarting;
        Commit(
            BridgeEventTypes.MissionFailed,
            new { message.MissionId, message.Reason, failures = _state.Failures });
        return Accepted(ControllerAction.RestartMission, "Retrying the same locked mission.");
    }

    private EngineResult HandleMissionCompleted(BridgeEnvelope message)
    {
        if (!MessageTargetsCurrentMission(message))
        {
            return RejectAfterDedupe(message, "Completion does not match the locked mission.");
        }

        if (_state.Phase != RunPhase.Running)
        {
            return RejectAfterDedupe(message, "Completion was reported outside the running mission.");
        }

        StopGameplayTimer();
        MissionDefinition completed = GetCurrentMission()
            ?? throw new InvalidOperationException("Current mission disappeared.");
        _state.CompletedMissionIds.Add(completed.Id);
        _state.CurrentIndex++;

        if (_state.CurrentIndex >= _state.MissionOrder.Count)
        {
            _state.Phase = RunPhase.Finished;
            _state.EndedUtc = _clock.UtcNow;
            _state.ActiveBridgeSessionId = null;
            StopRunTimer();
            Commit(
                BridgeEventTypes.MissionCompleted,
                new { message.MissionId, final = true });
            return Accepted(ControllerAction.FinishRun, "Final mission completed.");
        }

        MissionDefinition next = GetCurrentMission()
            ?? throw new InvalidOperationException("Next mission disappeared.");
        bool reuseCurrentBridge = string.Equals(
            completed.Game,
            next.Game,
            StringComparison.Ordinal);
        _state.Phase = reuseCurrentBridge ? RunPhase.Preparing : RunPhase.Transitioning;
        if (!reuseCurrentBridge)
        {
            _state.ActiveBridgeSessionId = null;
        }
        _state.ActiveGame = next.Game;
        Commit(
            BridgeEventTypes.MissionCompleted,
            new { message.MissionId, final = false, nextGame = next.Game });
        return Accepted(
            reuseCurrentBridge
                ? ControllerAction.PrepareMission
                : ControllerAction.LaunchNextMission,
            "Advancing to the next locked mission.");
    }

    private EngineResult HandleBridgeError(BridgeEnvelope message)
    {
        StopGameplayTimer();
        _state.Phase = RunPhase.Aborted;
        _state.EndedUtc = _clock.UtcNow;
        StopRunTimer();
        Commit(BridgeEventTypes.BridgeError, new { message.Reason });
        return Accepted(ControllerAction.AbortRun, "Bridge error invalidated the run.");
    }

    private EngineResult RejectAfterDedupe(BridgeEnvelope message, string reason)
    {
        string dedupeKey = MessageDedupeKey(message);
        _acceptedMessageIds.Remove(dedupeKey);
        _state.AcceptedMessageIds.Remove(dedupeKey);
        return Reject(reason);
    }

    private static string MessageDedupeKey(BridgeEnvelope message)
    {
        // Message ids are generated independently by each game adapter and
        // restart at b1 for every new bridge instance. Scope duplicate
        // detection to that instance so a destination game's b1 cannot be
        // mistaken for the completed game's b1 during a cross-game handoff.
        return string.Concat(
            message.Game,
            "\u001f",
            message.BridgeSessionId,
            "\u001f",
            message.MessageId);
    }

    private bool MessageTargetsCurrentMission(BridgeEnvelope message)
    {
        MissionDefinition? current = GetCurrentMission();
        return current is not null &&
               string.Equals(message.MissionId, current.Id, StringComparison.Ordinal);
    }

    private MissionDefinition? GetCurrentMission()
    {
        if (_state.CurrentIndex < 0 || _state.CurrentIndex >= _state.MissionOrder.Count)
        {
            return null;
        }

        return _catalog.GetMission(_state.MissionOrder[_state.CurrentIndex]);
    }

    private void StartGameplayTimer()
    {
        if (_gameplayStartedTimestamp is not null)
        {
            return;
        }

        _gameplayStartedTimestamp = _clock.Timestamp;
        _state.GameplayTimerRunning = true;
    }

    private void StopGameplayTimer()
    {
        if (_gameplayStartedTimestamp is long started)
        {
            long milliseconds = (long)_clock.Elapsed(started, _clock.Timestamp).TotalMilliseconds;
            _state.GameplayElapsedMilliseconds += Math.Max(0, milliseconds);
        }

        _gameplayStartedTimestamp = null;
        _state.GameplayTimerRunning = false;
    }

    private void StopRunTimer()
    {
        if (_runStartedTimestamp is long started)
        {
            _state.RealElapsedMilliseconds =
                Math.Max(0, (long)_clock.Elapsed(started, _clock.Timestamp).TotalMilliseconds);
        }

        _runStartedTimestamp = null;
    }

    private RunSnapshot CreateLiveSnapshot()
    {
        RunSnapshot snapshot = _state.Copy();
        long now = _clock.Timestamp;

        if (_runStartedTimestamp is long runStarted)
        {
            snapshot.RealElapsedMilliseconds =
                Math.Max(0, (long)_clock.Elapsed(runStarted, now).TotalMilliseconds);
        }

        if (_gameplayStartedTimestamp is long gameplayStarted)
        {
            snapshot.GameplayElapsedMilliseconds +=
                Math.Max(0, (long)_clock.Elapsed(gameplayStarted, now).TotalMilliseconds);
        }

        return snapshot;
    }

    private void Commit(string eventName, object? details = null)
    {
        _state.LastEventUtc = _clock.UtcNow;
        _state.AuditSequence++;
        RunSnapshot snapshot = CreateLiveSnapshot();
        _state.RealElapsedMilliseconds = snapshot.RealElapsedMilliseconds;
        _audit.Append(
            _state.RunId,
            _state.AuditSequence,
            _state.LastEventUtc,
            eventName,
            details);
        _persistence.Save(snapshot);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<string> ShuffleMissionIds(
        IEnumerable<string> missionIds,
        uint seed)
    {
        List<string> ids = missionIds.ToList();
        XorShift32 random = new(seed);

        string[] trilogyMissionIds = ids
            .Where(missionId =>
                TrilogyGames.Contains(_catalog.GetMission(missionId).Game))
            .ToArray();
        string? openingMission = trilogyMissionIds.Length == 0
            ? null
            : trilogyMissionIds[random.NextInt(trilogyMissionIds.Length)];
        if (openingMission is not null)
        {
            ids.Remove(openingMission);
        }

        for (int index = ids.Count - 1; index > 0; index--)
        {
            int swapIndex = random.NextInt(index + 1);
            (ids[index], ids[swapIndex]) = (ids[swapIndex], ids[index]);
        }

        if (openingMission is not null)
        {
            ids.Insert(0, openingMission);
        }

        return ids;
    }

    private List<string> ValidateMissionIds(
        IReadOnlyList<string> missionIds,
        string runDescription)
    {
        if (missionIds.Count < 1)
        {
            throw new ArgumentException(
                $"{runDescription} requires at least one mission.",
                nameof(missionIds));
        }

        List<string> selected = [];
        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string missionId in missionIds)
        {
            _catalog.GetMission(missionId);
            if (!unique.Add(missionId))
            {
                throw new ArgumentException(
                    $"{runDescription} cannot contain a duplicate mission.",
                    nameof(missionIds));
            }
            selected.Add(missionId);
        }
        return selected;
    }

    private void EnsureNoActiveRun()
    {
        if (_state.Phase is not RunPhase.Idle && !_state.IsTerminal)
        {
            throw new InvalidOperationException("A run is already active.");
        }
    }

    private RunSnapshot StartLockedRun(
        string mode,
        uint internalSeed,
        List<string> missionOrder)
    {
        DateTimeOffset now = _clock.UtcNow;
        _acceptedMessageIds.Clear();
        _runStartedTimestamp = _clock.Timestamp;
        _gameplayStartedTimestamp = null;
        _state = new RunSnapshot
        {
            RunId = Guid.NewGuid().ToString("N"),
            Mode = mode,
            InternalSeed = internalSeed,
            Phase = RunPhase.Launching,
            MissionOrder = missionOrder,
            StartedUtc = now,
            LastEventUtc = now,
            ActiveGame = _catalog.GetMission(missionOrder[0]).Game
        };

        Commit(
            "runStarted",
            new
            {
                mode,
                missionCount = missionOrder.Count,
                orderHash = HashMissionOrder(missionOrder)
            });
        return CreateLiveSnapshot();
    }

    private static uint CreateSeed()
    {
        return BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint)));
    }

    private static string HashMissionOrder(IEnumerable<string> missionIds)
    {
        byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", missionIds));
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static EngineResult Accepted(ControllerAction action, string reason)
    {
        return new EngineResult(MessageDisposition.Accepted, action, reason);
    }

    private static EngineResult Reject(string reason)
    {
        return new EngineResult(MessageDisposition.Rejected, ControllerAction.None, reason);
    }

    private sealed class XorShift32
    {
        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed == 0 ? 0x6d2b79f5u : seed;
        }

        public uint NextUInt32()
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }

        public int NextInt(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
            }

            uint bound = (uint)exclusiveUpperBound;
            uint threshold = unchecked(0u - bound) % bound;
            uint value;
            do
            {
                value = NextUInt32();
            }
            while (value < threshold);

            return (int)(value % bound);
        }
    }
}
