using Ghmr.Core.Launch;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ghmr.Controller.Launch;

internal static class GameWindowFocus
{
    private const int SwRestore = 9;

    public static bool TryFindMainWindow(
        GameLaunchProfile profile,
        out nint windowHandle)
    {
        ArgumentNullException.ThrowIfNull(profile);
        windowHandle = nint.Zero;
        Process[] matches = Process.GetProcessesByName(profile.ProcessName);

        try
        {
            foreach (Process process in matches)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    process.Refresh();
                    nint candidate = process.MainWindowHandle;
                    if (candidate == nint.Zero || !IsWindowVisible(candidate))
                    {
                        continue;
                    }

                    // More than one visible game window is unsafe to choose
                    // between. Leave focus alone and retry after the platform
                    // launch chain has settled.
                    if (windowHandle != nint.Zero && windowHandle != candidate)
                    {
                        windowHandle = nint.Zero;
                        return false;
                    }

                    windowHandle = candidate;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception)
                {
                    // A launcher-owned process can exit between enumeration
                    // and inspection. The next timer pass will rescan it.
                }
            }

            return windowHandle != nint.Zero;
        }
        finally
        {
            foreach (Process process in matches)
            {
                process.Dispose();
            }
        }
    }

    public static bool TryActivate(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !IsWindow(windowHandle))
        {
            return false;
        }

        _ = ShowWindowAsync(windowHandle, SwRestore);

        nint foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == windowHandle)
        {
            return true;
        }

        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = foregroundWindow == nint.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        uint gameThread = GetWindowThreadProcessId(windowHandle, out _);
        bool attachedToForeground = false;
        bool attachedToGame = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedToForeground = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    true);
            }

            if (gameThread != 0 &&
                gameThread != currentThread &&
                gameThread != foregroundThread)
            {
                attachedToGame = AttachThreadInput(
                    currentThread,
                    gameThread,
                    true);
            }

            _ = BringWindowToTop(windowHandle);
            _ = SetForegroundWindow(windowHandle);
            return GetForegroundWindow() == windowHandle;
        }
        finally
        {
            if (attachedToGame)
            {
                _ = AttachThreadInput(currentThread, gameThread, false);
            }

            if (attachedToForeground)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint idAttach,
        uint idAttachTo,
        [MarshalAs(UnmanagedType.Bool)] bool attach);
}
