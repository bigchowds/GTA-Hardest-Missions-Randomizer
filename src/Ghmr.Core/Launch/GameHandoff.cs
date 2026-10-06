namespace Ghmr.Core.Launch;

public sealed record GameLaunchProfile
{
    public required string Game { get; init; }
    public required string LaunchExecutablePath { get; init; }
    public required string ProcessName { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Game);
        ArgumentException.ThrowIfNullOrWhiteSpace(LaunchExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProcessName);

        if (ProcessName.Contains('/') || ProcessName.Contains('\\'))
        {
            throw new InvalidDataException(
                "The game process name must be a filename, not a path.");
        }

        if (ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The game process name must not include the .exe extension.");
        }
    }
}

public enum GameProcessCloseOutcome
{
    Closed,
    AlreadyClosed,
    ManualCloseRequired,
    TimedOut
}

public sealed record GameProcessCloseResult(
    GameProcessCloseOutcome Outcome,
    string Detail);

public interface IGameLaunchProfileProvider
{
    GameLaunchProfile? Find(string game);
}

public interface IGameProcessManager
{
    Task LaunchAsync(
        GameLaunchProfile profile,
        CancellationToken cancellationToken = default);

    Task<GameProcessCloseResult> RequestGracefulCloseAsync(
        GameLaunchProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface ITransitionDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

// Optional capability used only after an accepted GTA V completion advances
// to another game. Ordinary closure and final-mission handling stay separate.
public interface ICompletedGameProcessManager : IGameProcessManager
{
    Task<GameProcessCloseResult> RequestCompletedGameCloseAsync(
        GameLaunchProfile profile, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class SystemTransitionDelay : ITransitionDelay
{
    public Task WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        return Task.Delay(delay, cancellationToken);
    }
}

public enum GameHandoffOutcome
{
    Launched,
    Closed,
    ProfileMissing,
    ManualCloseRequired,
    CloseTimedOut
}

public sealed record GameHandoffResult(
    GameHandoffOutcome Outcome,
    string Game,
    string? NextGame,
    string Detail);

public enum GameHandoffStage
{
    ClosingCompletedGame,
    SettlingPlatform,
    LaunchingNextGame,
    NextGameDetected
}

public sealed record GameHandoffProgress(
    GameHandoffStage Stage,
    string CompletedGame,
    string NextGame);

/// <summary>
/// Coordinates game-to-game transitions. A completed GTA V replay can use a
/// dedicated close capability to avoid its interactive quit confirmation.
/// Other games and explicit final closures use normal window-close requests.
/// </summary>
public sealed class GameHandoffService
{
    private readonly IGameLaunchProfileProvider _profiles;
    private readonly IGameProcessManager _processes;
    private readonly ITransitionDelay _delay;
    private readonly TimeSpan _fadeDelay;
    private readonly TimeSpan _closeTimeout;
    private readonly TimeSpan _launcherSettleDelay;

    public GameHandoffService(
        IGameLaunchProfileProvider profiles,
        IGameProcessManager processes,
        ITransitionDelay? delay = null,
        TimeSpan? fadeDelay = null,
        TimeSpan? closeTimeout = null,
        TimeSpan? launcherSettleDelay = null)
    {
        _profiles = profiles;
        _processes = processes;
        _delay = delay ?? new SystemTransitionDelay();
        _fadeDelay = fadeDelay ?? TimeSpan.FromMilliseconds(900);
        _closeTimeout = closeTimeout ?? TimeSpan.FromSeconds(20);
        _launcherSettleDelay = launcherSettleDelay ?? TimeSpan.FromSeconds(3);
    }

    public async Task<GameHandoffResult> LaunchInitialAsync(
        string game,
        CancellationToken cancellationToken = default)
    {
        GameLaunchProfile? profile = _profiles.Find(game);
        if (profile is null)
        {
            return MissingProfile(game, nextGame: game);
        }

        await _processes.LaunchAsync(profile, cancellationToken)
            .ConfigureAwait(false);
        return new GameHandoffResult(
            GameHandoffOutcome.Launched,
            game,
            game,
            "Initial game launched.");
    }

    public async Task<GameHandoffResult> TransitionAsync(
        string completedGame,
        string nextGame,
        CancellationToken cancellationToken = default,
        Action<GameHandoffProgress>? reportProgress = null)
    {
        // Resolve both profiles before closing anything. If setup is incomplete,
        // the current game stays open and the player remains in control.
        GameLaunchProfile? completedProfile = _profiles.Find(completedGame);
        GameLaunchProfile? nextProfile = _profiles.Find(nextGame);
        if (completedProfile is null || nextProfile is null)
        {
            return MissingProfile(
                completedProfile is null ? completedGame : nextGame,
                nextGame);
        }

        reportProgress?.Invoke(new GameHandoffProgress(
            GameHandoffStage.ClosingCompletedGame,
            completedGame,
            nextGame));
        await _delay.WaitAsync(_fadeDelay, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        GameProcessCloseResult close = completedGame == "gtav_enhanced" &&
            _processes is ICompletedGameProcessManager completedProcesses
            ? await completedProcesses.RequestCompletedGameCloseAsync(
                completedProfile, _closeTimeout, cancellationToken).ConfigureAwait(false)
            : await _processes.RequestGracefulCloseAsync(
                completedProfile,
                _closeTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (close.Outcome == GameProcessCloseOutcome.ManualCloseRequired)
        {
            return new GameHandoffResult(
                GameHandoffOutcome.ManualCloseRequired,
                completedGame,
                nextGame,
                close.Detail);
        }

        if (close.Outcome == GameProcessCloseOutcome.TimedOut)
        {
            return new GameHandoffResult(
                GameHandoffOutcome.CloseTimedOut,
                completedGame,
                nextGame,
                close.Detail);
        }

        return await LaunchAfterCloseAsync(completedGame, nextProfile, cancellationToken, reportProgress)
            .ConfigureAwait(false);
    }

    // Called only when the coordinator has observed the already-completed
    // outgoing process exit after an unsuccessful automatic close.
    public async Task<GameHandoffResult> ResumeAfterCompletedGameExitAsync(
        string completedGame, string nextGame,
        CancellationToken cancellationToken = default,
        Action<GameHandoffProgress>? reportProgress = null)
    {
        GameLaunchProfile? nextProfile = _profiles.Find(nextGame);
        if (nextProfile is null) return MissingProfile(nextGame, nextGame);
        return await LaunchAfterCloseAsync(completedGame, nextProfile, cancellationToken, reportProgress)
            .ConfigureAwait(false);
    }

    private async Task<GameHandoffResult> LaunchAfterCloseAsync(
        string completedGame, GameLaunchProfile nextProfile,
        CancellationToken cancellationToken,
        Action<GameHandoffProgress>? reportProgress)
    {
        string nextGame = nextProfile.Game;
        cancellationToken.ThrowIfCancellationRequested();
        // Keep a short cleanup window after the visible process exits. The next
        // title now launches through its owning platform (Steam App ID or the
        // Rockstar commerce-provider argument), so the former ten-second delay
        // is no longer used to mask a bare-executable ownership handoff.
        reportProgress?.Invoke(new GameHandoffProgress(
            GameHandoffStage.SettlingPlatform,
            completedGame,
            nextGame));
        await _delay.WaitAsync(_launcherSettleDelay, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        reportProgress?.Invoke(new GameHandoffProgress(
            GameHandoffStage.LaunchingNextGame,
            completedGame,
            nextGame));
        await _processes.LaunchAsync(nextProfile, cancellationToken)
            .ConfigureAwait(false);
        reportProgress?.Invoke(new GameHandoffProgress(
            GameHandoffStage.NextGameDetected,
            completedGame,
            nextGame));
        return new GameHandoffResult(
            GameHandoffOutcome.Launched,
            completedGame,
            nextGame,
            "Next game launched.");
    }

    public async Task<GameHandoffResult> FinishAsync(
        string completedGame,
        CancellationToken cancellationToken = default)
    {
        GameLaunchProfile? profile = _profiles.Find(completedGame);
        if (profile is null)
        {
            return MissingProfile(completedGame, nextGame: null);
        }

        await _delay.WaitAsync(_fadeDelay, cancellationToken)
            .ConfigureAwait(false);
        GameProcessCloseResult close = await _processes.RequestGracefulCloseAsync(
                profile,
                _closeTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        return close.Outcome switch
        {
            GameProcessCloseOutcome.Closed or GameProcessCloseOutcome.AlreadyClosed =>
                new GameHandoffResult(
                    GameHandoffOutcome.Closed,
                    completedGame,
                    null,
                    close.Detail),
            GameProcessCloseOutcome.ManualCloseRequired =>
                new GameHandoffResult(
                    GameHandoffOutcome.ManualCloseRequired,
                    completedGame,
                    null,
                    close.Detail),
            GameProcessCloseOutcome.TimedOut =>
                new GameHandoffResult(
                    GameHandoffOutcome.CloseTimedOut,
                    completedGame,
                    null,
                    close.Detail),
            _ => throw new InvalidOperationException("Unknown game close outcome.")
        };
    }

    private static GameHandoffResult MissingProfile(
        string missingGame,
        string? nextGame)
    {
        return new GameHandoffResult(
            GameHandoffOutcome.ProfileMissing,
            missingGame,
            nextGame,
            "A required game launch profile is not configured.");
    }
}
