using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ghmr.Controller.Input;

public enum Gta5ReplayMenuAutomationOutcome
{
    DerailedHighlighted,
    GameProcessMissing,
    GameWindowMissing,
    FocusDenied,
    InputRejected
}

public sealed record Gta5ReplayMenuAutomationResult(
    Gta5ReplayMenuAutomationOutcome Outcome,
    string Detail);

public interface IGta5ReplayMenuAutomator
{
    Task<Gta5ReplayMenuAutomationResult> HighlightDerailedAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Drives GTA V Enhanced's native pause menu with ordinary keyboard input.
/// The calibration build deliberately stops with Derailed highlighted: the
/// player confirms the highlighted title before a later build is allowed to
/// press Enter and start the mission automatically.
/// </summary>
public sealed class WindowsGta5ReplayMenuAutomator : IGta5ReplayMenuAutomator
{
    private const string ProcessName = "GTA5_Enhanced";
    private const int GameTabOffsetFromMap = 4;
    private const int DerailedZeroBasedReplayIndex = 46;
    private const int ReplayListResetPresses = 80;
    private const int KeyHoldMilliseconds = 85;

    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyEnter = 0x0D;
    private const ushort VirtualKeyUp = 0x26;
    private const ushort VirtualKeyDown = 0x28;
    private const ushort VirtualKeyE = 0x45;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const int ShowWindowRestore = 9;

    private static readonly TimeSpan FocusTimeout = TimeSpan.FromSeconds(12);
    private readonly string _logPath;

    public WindowsGta5ReplayMenuAutomator(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _logPath = Path.Combine(dataDirectory, "gtav-menu-automation.log");
    }

    public async Task<Gta5ReplayMenuAutomationResult> HighlightDerailedAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using ProcessLease lease = FindGameProcess();
            if (lease.Ambiguous || lease.Process is null)
            {
                return Finish(
                    Gta5ReplayMenuAutomationOutcome.GameProcessMissing,
                    lease.Ambiguous
                        ? "More than one GTA5_Enhanced process was found."
                        : "GTA5_Enhanced is not running.");
            }

            Process game = lease.Process;
            nint window = await WaitForMainWindowAsync(game, cancellationToken)
                .ConfigureAwait(false);
            if (window == nint.Zero)
            {
                return Finish(
                    Gta5ReplayMenuAutomationOutcome.GameWindowMissing,
                    "GTA V did not expose a main window in time.");
            }

            if (!await FocusGameAsync(game, window, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Finish(
                    Gta5ReplayMenuAutomationOutcome.FocusDenied,
                    "Windows did not give keyboard focus to GTA V, so no menu input was sent.");
            }

            Log("GTA V focused; starting guarded Derailed menu calibration");
            await PressAsync(VirtualKeyEscape, 1500, game.Id, cancellationToken)
                .ConfigureAwait(false);
            Log("Sent Escape with a frame-visible hold; pause menu should now be open");

            // GTA V opens the pause menu on Map. E advances one top-level tab:
            // Map -> Brief -> Stats -> Settings -> Game.
            for (int index = 0; index < GameTabOffsetFromMap; index++)
            {
                await PressAsync(VirtualKeyE, 220, game.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            Log("Sent four held E presses; Game tab should now be selected");

            await PressAsync(VirtualKeyEnter, 1100, game.Id, cancellationToken)
                .ConfigureAwait(false);
            Log("Sent held Enter; Replay Mission list should now be open");

            // Do not use Home: GTA V reserves it for the Rockstar overlay.
            // Repeated Up presses clamp safely at the first replay entry.
            for (int index = 0; index < ReplayListResetPresses; index++)
            {
                await PressAsync(VirtualKeyUp, 45, game.Id, cancellationToken)
                    .ConfigureAwait(false);
            }

            for (int index = 0; index < DerailedZeroBasedReplayIndex; index++)
            {
                await PressAsync(VirtualKeyDown, 65, game.Id, cancellationToken)
                    .ConfigureAwait(false);
            }

            Log(
                "Calibration input route completed; stopped before confirmation at " +
                $"candidate replay index {DerailedZeroBasedReplayIndex}. " +
                "The controller cannot visually verify the highlighted title.");
            return Finish(
                Gta5ReplayMenuAutomationOutcome.DerailedHighlighted,
                "Menu automation stopped safely before confirmation.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception)
        {
            return Finish(
                Gta5ReplayMenuAutomationOutcome.InputRejected,
                $"Windows rejected automated keyboard input: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return Finish(
                Gta5ReplayMenuAutomationOutcome.InputRejected,
                exception.Message);
        }
    }

    private static ProcessLease FindGameProcess()
    {
        Process[] matches = Process.GetProcessesByName(ProcessName);
        if (matches.Length == 1)
        {
            return new ProcessLease(matches[0], [], ambiguous: false);
        }

        return new ProcessLease(null, matches, ambiguous: matches.Length > 1);
    }

    private static async Task<nint> WaitForMainWindowAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.Elapsed < FocusTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            nint handle = process.MainWindowHandle;
            if (handle != nint.Zero)
            {
                return handle;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        return nint.Zero;
    }

    private static async Task<bool> FocusGameAsync(
        Process process,
        nint window,
        CancellationToken cancellationToken)
    {
        ShowWindowAsync(window, ShowWindowRestore);
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.Elapsed < FocusTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetForegroundWindow(window);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
            if (ForegroundBelongsTo(process.Id))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task PressAsync(
        ushort virtualKey,
        int delayAfterMilliseconds,
        int expectedProcessId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ForegroundBelongsTo(expectedProcessId))
        {
            throw new InvalidOperationException(
                "GTA V lost focus; menu automation stopped before sending another key.");
        }

        // GTA V polls keyboard state once per rendered frame. Sending key-down
        // and key-up in one SendInput batch can therefore succeed at the Win32
        // layer while remaining invisible to the game. Hold every key across
        // several frames, and always release it before honoring cancellation.
        SendKeyboardEvent(virtualKey, flags: 0);
        await Task.Delay(
                TimeSpan.FromMilliseconds(KeyHoldMilliseconds),
                CancellationToken.None)
            .ConfigureAwait(false);
        SendKeyboardEvent(virtualKey, KeyEventKeyUp);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Delay(
                TimeSpan.FromMilliseconds(delayAfterMilliseconds),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void SendKeyboardEvent(ushort virtualKey, uint flags)
    {
        INPUT[] inputs = [KeyboardInput(virtualKey, flags)];
        uint sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static INPUT KeyboardInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }

    private static bool ForegroundBelongsTo(int expectedProcessId)
    {
        nint foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(foreground, out uint processId);
        return processId == (uint)expectedProcessId;
    }

    private Gta5ReplayMenuAutomationResult Finish(
        Gta5ReplayMenuAutomationOutcome outcome,
        string detail)
    {
        Log($"Automation result={outcome}; {detail}");
        return new Gta5ReplayMenuAutomationResult(outcome, detail);
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(
                _logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [GHMR] {message}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
#pragma warning restore CS0649

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        [In] INPUT[] inputs,
        int sizeOfInputStructure);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

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
