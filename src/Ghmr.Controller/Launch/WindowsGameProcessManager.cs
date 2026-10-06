using Ghmr.Core.Launch;
using Ghmr.Controller.Diagnostics;
using System.Diagnostics;

namespace Ghmr.Controller.Launch;

public sealed class WindowsGameProcessManager : ICompletedGameProcessManager, IGameProcessExitMonitor
{
    private const string Gta3DefinitiveGameId = "gta3de";
    private const string Gta3DefinitiveSteamLaunchUri = "steam://rungameid/1546970";
    private const string ViceCityDefinitiveGameId = "vcde";
    private const string ViceCityDefinitiveSteamLaunchUri = "steam://rungameid/1546990";
    private const string SanAndreasDefinitiveGameId = "sade";
    private const string SanAndreasDefinitiveSteamLaunchUri = "steam://rungameid/1547000";
    private const string RockstarCommerceProviderArgument = "-scCommerceProvider=4";
    private const string Gta4GameId = "gta4";
    private const string Gta4SteamLaunchUri = "steam://rungameid/12210";
    private const string Gta5EnhancedGameId = "gtav_enhanced";
    private const string Gta5EnhancedSteamLaunchUri = "steam://rungameid/3240220";
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CloseRetryInterval = TimeSpan.FromSeconds(2);
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _trackedProcessIds = new(StringComparer.Ordinal);
    private readonly ControllerDiagnosticLog? _log;

    public WindowsGameProcessManager(ControllerDiagnosticLog? log = null)
    {
        _log = log;
    }

    public async Task<bool> WaitForExitAsync(
        GameLaunchProfile profile,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        int? trackedId = GetTracked(profile.Game);
        using ProcessLease lease = FindProcess(profile);
        if (lease.Ambiguous)
        {
            _log?.Warning("ProcessExit", $"Exit watch unavailable; game={profile.Game}; ambiguous process match");
            return false;
        }
        Process? process = lease.Process;
        if (process is null)
        {
            _log?.Warning("ProcessExit", $"No process found after bridge acceptance; " +
                $"game={profile.Game}; trackedPid={trackedId}");
            return trackedId.HasValue;
        }

        int processId = process.Id;
        _log?.Info("ProcessExit", $"Watching active gameplay process; game={profile.Game}; pid={processId}");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _log?.Info("ProcessExit", $"Gameplay process exited; game={profile.Game}; pid={processId}");
        return true;
    }

    public async Task LaunchAsync(
        GameLaunchProfile profile,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        _log?.Info("Launch", $"Launch requested; game={profile.Game}; " +
            $"processName={profile.ProcessName}; executable={Path.GetFileName(profile.LaunchExecutablePath)}");
        if (!File.Exists(profile.LaunchExecutablePath))
        {
            throw new FileNotFoundException(
                "The configured game executable no longer exists.");
        }

        using ProcessLease existing = FindProcess(profile);
        if (existing.Ambiguous)
        {
            throw new InvalidOperationException(
                "More than one matching game process is running; close them before starting.");
        }
        if (existing.Process is not null)
        {
            throw new InvalidOperationException(
                "The configured game already appears to be running.");
        }

        ProcessStartInfo startInfo = CreateLaunchStartInfo(profile);
        _log?.Info("Launch", $"Launch route={startInfo.FileName}; arguments={startInfo.Arguments}");
        RockstarStartupDialogAutoConfirmer.Start(profile.Game, cancellationToken);
        using Process? started = Process.Start(startInfo);
        Stopwatch timer = Stopwatch.StartNew();

        while (timer.Elapsed < LaunchTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ProcessLease found = FindProcess(profile);
            if (found.Process is not null)
            {
                Track(profile.Game, found.Process.Id);
                _log?.Info("Launch", $"Game process detected; game={profile.Game}; " +
                    $"pid={found.Process.Id}; elapsedMs={timer.ElapsedMilliseconds}");
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
        }

        _log?.Warning("Launch", $"Game process discovery timed out; game={profile.Game}; " +
            $"processName={profile.ProcessName}; elapsedMs={timer.ElapsedMilliseconds}");
        throw new TimeoutException(
            "The game process did not appear within two minutes. Check its configured process name.");
    }

    private static ProcessStartInfo CreateLaunchStartInfo(
        GameLaunchProfile profile)
    {
        // A Steam-owned Definitive Edition title must enter through its Steam
        // App ID. Starting the Unreal gameplay EXE directly bypasses Steam's
        // ownership handoff and can leave Rockstar's "Connecting to Social
        // Club" prompt or a permanently stalled splash screen behind.
        if (IsSteamInstall(profile.LaunchExecutablePath) &&
            TryGetDefinitiveEditionSteamLaunchUri(profile.Game, out string steamUri))
        {
            return new ProcessStartInfo
            {
                FileName = steamUri,
                UseShellExecute = true
            };
        }

        // Starting a Steam-owned GTAIV.exe directly makes Steam display its
        // "Launch Game with custom arguments" confirmation. Launching the
        // registered Steam App ID avoids that prompt; the normal process scan
        // below still discovers and tracks the real GTA IV process.
        if (string.Equals(profile.Game, Gta4GameId, StringComparison.Ordinal) &&
            IsSteamInstall(profile.LaunchExecutablePath))
        {
            return new ProcessStartInfo
            {
                FileName = Gta4SteamLaunchUri,
                UseShellExecute = true
            };
        }

        // GTA V Enhanced is also launcher-owned.  Starting its gameplay EXE
        // directly from a Steam library can produce the same custom-arguments
        // confirmation seen in GTA IV.  Use its registered Steam App ID while
        // retaining the configured gameplay process for discovery/tracking.
        if (string.Equals(
                profile.Game,
                Gta5EnhancedGameId,
                StringComparison.Ordinal) &&
            IsSteamInstall(profile.LaunchExecutablePath))
        {
            return new ProcessStartInfo
            {
                FileName = Gta5EnhancedSteamLaunchUri,
                UseShellExecute = true
            };
        }

        ProcessStartInfo directStart = new()
        {
            FileName = profile.LaunchExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(profile.LaunchExecutablePath)
                ?? AppContext.BaseDirectory,
            UseShellExecute = true
        };

        // Rockstar-owned Definitive Edition executables use the commerce
        // provider argument published in Rockstar's title metadata. The
        // dialog watcher remains armed only as a defensive fallback.
        if (IsDefinitiveEditionGame(profile.Game))
        {
            directStart.Arguments = RockstarCommerceProviderArgument;
        }

        return directStart;
    }

    private static bool TryGetDefinitiveEditionSteamLaunchUri(
        string game,
        out string launchUri)
    {
        launchUri = game switch
        {
            Gta3DefinitiveGameId => Gta3DefinitiveSteamLaunchUri,
            ViceCityDefinitiveGameId => ViceCityDefinitiveSteamLaunchUri,
            SanAndreasDefinitiveGameId => SanAndreasDefinitiveSteamLaunchUri,
            _ => string.Empty
        };
        return launchUri.Length > 0;
    }

    private static bool IsDefinitiveEditionGame(string game)
    {
        return game is Gta3DefinitiveGameId or ViceCityDefinitiveGameId or
            SanAndreasDefinitiveGameId;
    }

    private static bool IsSteamInstall(string executablePath)
    {
        string normalized = executablePath.Replace('/', '\\');
        return normalized.Contains(
            "\\steamapps\\common\\",
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GameProcessCloseResult> RequestCompletedGameCloseAsync(
        GameLaunchProfile profile, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (profile.Game != Gta5EnhancedGameId)
            return await RequestGracefulCloseAsync(profile, timeout, cancellationToken).ConfigureAwait(false);
        profile.Validate();
        Stopwatch timer = Stopwatch.StartNew();
        _log?.Info("ProcessClose", "Completed GTA V handoff requested; bypassing interactive quit prompt.");
        cancellationToken.ThrowIfCancellationRequested();
        using ProcessLease lease = FindProcess(profile);
        if (lease.Ambiguous)
            return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.ManualCloseRequired,
                "Completed GTA V could not be identified uniquely.");
        Process? process = lease.Process;
        if (process is null)
        {
            Forget(profile.Game);
            return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.AlreadyClosed,
                "The completed GTA V process was already closed.");
        }
        try
        {
            // Pin this process handle before checking identity. Do not act on
            // another process if a PID is later reused.
            _ = process.Handle;
            if (process.HasExited)
            {
                Forget(profile.Game);
                return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.AlreadyClosed,
                    "The completed GTA V process was already closed.");
            }
            if (!CompletedGameClosePolicy.CanTerminateGtaV(
                    profile, process.ProcessName, process.MainModule?.FileName))
                return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.ManualCloseRequired,
                    "GTA V executable identity could not be verified; close the completed game manually.");
            cancellationToken.ThrowIfCancellationRequested();
            _log?.Info("ProcessClose", $"Ending completed replay process; game={profile.Game}; pid={process.Id}; processTree=false");
            // The mission pass has already been persisted by the engine.
            // End only this verified gameplay process, never Steam/Rockstar
            // or its process tree. This discards unsaved replay state.
            process.Kill(entireProcessTree: false);
            using CancellationTokenSource exitDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            exitDeadline.CancelAfter(timeout < TimeSpan.FromSeconds(10) ? timeout : TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(exitDeadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.TimedOut,
                    "Completed GTA V exit is still pending; waiting for manual closure.");
            }
            Forget(profile.Game);
            return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.Closed,
                "The verified completed GTA V process exited without a quit confirmation.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A race with Task Manager is successful only if the same pinned
            // process is known to have exited. Otherwise keep the next launch
            // blocked and let the coordinator watch for manual closure.
            bool exited = false;
            try { exited = process.HasExited; }
            catch (InvalidOperationException) { }
            if (exited)
            {
                Forget(profile.Game);
                return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.AlreadyClosed,
                    "The completed GTA V process exited while closure was requested.");
            }
            _log?.Error("ProcessClose", "Completed GTA V close failed; manual closure required", exception);
            return ReportCloseResult(profile.Game, timer, GameProcessCloseOutcome.ManualCloseRequired,
                "GTA V could not be closed automatically; close it manually to continue.");
        }
    }

    public async Task<GameProcessCloseResult> RequestGracefulCloseAsync(
        GameLaunchProfile profile,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Stopwatch operationTimer = Stopwatch.StartNew();
        profile.Validate();
        _log?.Info("ProcessClose", $"Close requested; game={profile.Game}; " +
            $"processName={profile.ProcessName}; timeoutMs={timeout.TotalMilliseconds}");
        using ProcessLease lease = FindProcess(profile);
        if (lease.Ambiguous)
        {
            return ReportCloseResult(profile.Game, operationTimer,
                GameProcessCloseOutcome.ManualCloseRequired,
                "More than one matching game process is running.");
        }
        Process? process = lease.Process;
        if (process is null)
        {
            Forget(profile.Game);
            return ReportCloseResult(profile.Game, operationTimer,
                GameProcessCloseOutcome.AlreadyClosed,
                "The completed game was already closed.");
        }

        try
        {
            _log?.Info("ProcessClose", $"Close target matched; game={profile.Game}; pid={process.Id}");
            if (process.HasExited)
            {
                Forget(profile.Game);
                return ReportCloseResult(profile.Game, operationTimer,
                    GameProcessCloseOutcome.AlreadyClosed,
                    "The completed game was already closed.");
            }

            Task waitForExit = process.WaitForExitAsync(cancellationToken);
            Stopwatch closeTimer = Stopwatch.StartNew();
            bool closeRequestAccepted = false;
            int closeAttempt = 0;

            while (closeTimer.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                process.Refresh();
                if (process.HasExited)
                {
                    await waitForExit.ConfigureAwait(false);
                    Forget(profile.Game);
                    return ReportCloseResult(profile.Game, operationTimer,
                        GameProcessCloseOutcome.Closed,
                        "The completed game closed normally.");
                }

                // Repeat the normal close request while waiting for exit.
                // Record each result so an ignored close can be diagnosed.
                closeAttempt++;
                _log?.Info("ProcessClose", $"Sending normal window close; game={profile.Game}; " +
                    $"pid={process.Id}; attempt={closeAttempt}");
                bool requestAccepted = process.CloseMainWindow();
                closeRequestAccepted |= requestAccepted;
                _log?.Info("ProcessClose", $"Normal window close returned; game={profile.Game}; " +
                    $"attempt={closeAttempt}; accepted={requestAccepted}");

                TimeSpan remaining = timeout - closeTimer.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                TimeSpan retryDelay = remaining < CloseRetryInterval
                    ? remaining
                    : CloseRetryInterval;
                Task retry = Task.Delay(retryDelay, cancellationToken);
                Task finished = await Task.WhenAny(waitForExit, retry)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (ReferenceEquals(finished, waitForExit))
                {
                    await waitForExit.ConfigureAwait(false);
                    Forget(profile.Game);
                    return ReportCloseResult(profile.Game, operationTimer,
                        GameProcessCloseOutcome.Closed,
                        "The completed game closed normally.");
                }
            }

            if (!closeRequestAccepted)
            {
                return ReportCloseResult(profile.Game, operationTimer,
                    GameProcessCloseOutcome.ManualCloseRequired,
                    "The game did not expose a normal window-close action.");
            }

            return ReportCloseResult(profile.Game, operationTimer,
                GameProcessCloseOutcome.TimedOut,
                "The game did not close after repeated normal close requests.");
        }
        catch (InvalidOperationException exception)
        {
            _log?.Error("ProcessClose", $"Process became unavailable while closing; " +
                $"game={profile.Game}", exception);
            Forget(profile.Game);
            return ReportCloseResult(profile.Game, operationTimer,
                GameProcessCloseOutcome.AlreadyClosed,
                "The completed game was already closed.");
        }
    }

    private GameProcessCloseResult ReportCloseResult(
        string game,
        Stopwatch timer,
        GameProcessCloseOutcome outcome,
        string detail)
    {
        _log?.Info("ProcessClose", $"Close result={outcome}; game={game}; " +
            $"elapsedMs={timer.ElapsedMilliseconds}; detail={detail}");
        return new GameProcessCloseResult(outcome, detail);
    }

    private ProcessLease FindProcess(GameLaunchProfile profile)
    {
        int? trackedId = GetTracked(profile.Game);
        if (trackedId.HasValue)
        {
            try
            {
                Process tracked = Process.GetProcessById(trackedId.Value);
                if (!tracked.HasExited &&
                    string.Equals(
                        tracked.ProcessName,
                        profile.ProcessName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessLease(tracked, [], ambiguous: false);
                }
                tracked.Dispose();
            }
            catch (ArgumentException)
            {
                Forget(profile.Game);
            }
            catch (InvalidOperationException)
            {
                Forget(profile.Game);
            }
        }

        Process[] matches = Process.GetProcessesByName(profile.ProcessName);
        if (matches.Length == 1)
        {
            return new ProcessLease(matches[0], [], ambiguous: false);
        }

        // Closing an arbitrary one of several same-named processes would be unsafe.
        // Dispose every candidate and report no uniquely identifiable process.
        return new ProcessLease(null, matches, ambiguous: matches.Length > 1);
    }

    private int? GetTracked(string game)
    {
        lock (_gate)
        {
            return _trackedProcessIds.TryGetValue(game, out int processId)
                ? processId
                : null;
        }
    }

    private void Track(string game, int processId)
    {
        lock (_gate)
        {
            _trackedProcessIds[game] = processId;
        }
    }

    private void Forget(string game)
    {
        lock (_gate)
        {
            _trackedProcessIds.Remove(game);
        }
    }

    private sealed class ProcessLease : IDisposable
    {
        private readonly IReadOnlyList<Process> _extraProcesses;

        public ProcessLease(
            Process? process,
            IReadOnlyList<Process> extraProcesses,
            bool ambiguous)
        {
            Process = process;
            _extraProcesses = extraProcesses;
            Ambiguous = ambiguous;
        }

        public Process? Process { get; }
        public bool Ambiguous { get; }

        public void Dispose()
        {
            Process?.Dispose();
            foreach (Process process in _extraProcesses)
            {
                process.Dispose();
            }
        }
    }
}
