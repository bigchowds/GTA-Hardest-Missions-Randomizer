namespace Ghmr.Core.Run;

public enum RunPhase
{
    Idle,
    Launching,
    Preparing,
    Running,
    Restarting,
    Transitioning,
    Finished,
    Aborted,
    Interrupted
}

public enum ControllerAction
{
    None,
    PrepareMission,
    StartMission,
    RestartMission,
    LaunchNextMission,
    FinishRun,
    AbortRun
}

public enum MessageDisposition
{
    Accepted,
    IgnoredDuplicate,
    Rejected
}

public sealed record EngineResult(
    MessageDisposition Disposition,
    ControllerAction Action,
    string Reason);

public sealed class RunSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RunId { get; set; } = string.Empty;
    public string Mode { get; set; } = "development";
    public uint InternalSeed { get; set; }
    public RunPhase Phase { get; set; } = RunPhase.Idle;
    public List<string> MissionOrder { get; set; } = [];
    public List<string> CompletedMissionIds { get; set; } = [];
    public int CurrentIndex { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public DateTimeOffset LastEventUtc { get; set; }
    public long RealElapsedMilliseconds { get; set; }
    public long GameplayElapsedMilliseconds { get; set; }
    public bool GameplayTimerRunning { get; set; }
    public string? ActiveBridgeSessionId { get; set; }
    public string? ActiveGame { get; set; }
    public List<string> AcceptedMessageIds { get; set; } = [];
    public long AuditSequence { get; set; }

    public bool IsTerminal => Phase is RunPhase.Finished or RunPhase.Aborted or RunPhase.Interrupted;

    public RunSnapshot Copy()
    {
        return new RunSnapshot
        {
            RunId = RunId,
            Mode = Mode,
            InternalSeed = InternalSeed,
            Phase = Phase,
            MissionOrder = [.. MissionOrder],
            CompletedMissionIds = [.. CompletedMissionIds],
            CurrentIndex = CurrentIndex,
            Failures = Failures,
            StartedUtc = StartedUtc,
            EndedUtc = EndedUtc,
            LastEventUtc = LastEventUtc,
            RealElapsedMilliseconds = RealElapsedMilliseconds,
            GameplayElapsedMilliseconds = GameplayElapsedMilliseconds,
            GameplayTimerRunning = GameplayTimerRunning,
            ActiveBridgeSessionId = ActiveBridgeSessionId,
            ActiveGame = ActiveGame,
            AcceptedMessageIds = [.. AcceptedMessageIds],
            AuditSequence = AuditSequence
        };
    }
}
