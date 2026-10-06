using System.Runtime.InteropServices;
using System.Text;

namespace Ghmr.Controller.Launch;

/// <summary>
/// Clears one known Rockstar Games Launcher message box that can block a
/// Definitive Edition title before CLEO Redux is allowed to load.
/// </summary>
internal static class RockstarStartupDialogAutoConfirmer
{
    private const string DialogClassName = "#32770";
    private const string ButtonClassName = "Button";
    private const string ConnectingText = "Connecting to Social Club";
    private const string ContinueText = "Press OK to continue or Cancel to quit.";
    private const int IdOk = 1;
    private const int IdCancel = 2;
    private const uint WmGetText = 0x000D;
    private const uint BmClick = 0x00F5;
    private const uint SmtoAbortIfHung = 0x0002;
    private const int TextCapacity = 512;
    private const uint MessageTimeoutMilliseconds = 100;

    private static readonly HashSet<string> SupportedGames = new(
        ["sade", "gta3de", "vcde"],
        StringComparer.Ordinal);

    private static readonly TimeSpan WatchDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    public static void Start(string game, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !SupportedGames.Contains(game))
        {
            return;
        }

        // WatchAsync catches every native-window failure internally. Discarding
        // this task keeps game-process discovery non-blocking while the message
        // box can still appear later in Rockstar's startup sequence.
        _ = WatchAsync(cancellationToken);
    }

    private static async Task WatchAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(WatchDuration);

        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                ConfirmMatchingDialogs();
                await Task.Delay(PollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // The expected end state is either the controller shutting down or
            // the short startup observation window expiring.
        }
        catch (DllNotFoundException)
        {
            // The controller is Windows-only, but a missing user32 entry must
            // never prevent a game from being launched manually.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void ConfirmMatchingDialogs()
    {
        EnumWindows(
            static (window, _) =>
            {
                TryConfirmDialog(window);
                return true;
            },
            IntPtr.Zero);
    }

    private static void TryConfirmDialog(IntPtr window)
    {
        if (!IsWindowVisible(window) ||
            !string.Equals(
                ReadClassName(window),
                DialogClassName,
                StringComparison.Ordinal))
        {
            return;
        }

        bool hasConnectingText = false;
        bool hasContinueText = false;
        IntPtr labelledOkButton = IntPtr.Zero;
        IntPtr labelledCancelButton = IntPtr.Zero;

        EnumChildWindows(
            window,
            (child, _) =>
            {
                string text = ReadWindowText(child);
                if (text.Contains(ConnectingText, StringComparison.OrdinalIgnoreCase))
                {
                    hasConnectingText = true;
                }
                if (text.Contains(ContinueText, StringComparison.OrdinalIgnoreCase))
                {
                    hasContinueText = true;
                }

                if (string.Equals(
                        ReadClassName(child),
                        ButtonClassName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    string buttonText = text.Replace("&", string.Empty, StringComparison.Ordinal)
                        .Trim();
                    if (string.Equals(buttonText, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        labelledOkButton = child;
                    }
                    else if (string.Equals(
                                 buttonText,
                                 "Cancel",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        labelledCancelButton = child;
                    }
                }

                return true;
            },
            IntPtr.Zero);

        if (!hasConnectingText || !hasContinueText)
        {
            return;
        }

        IntPtr okButton = GetDlgItem(window, IdOk);
        IntPtr cancelButton = GetDlgItem(window, IdCancel);
        if (okButton == IntPtr.Zero)
        {
            okButton = labelledOkButton;
        }
        if (cancelButton == IntPtr.Zero)
        {
            cancelButton = labelledCancelButton;
        }

        // Requiring both choices prevents a generic Social Club error with a
        // single acknowledgement button from being dismissed silently.
        if (okButton == IntPtr.Zero || cancelButton == IntPtr.Zero ||
            !string.Equals(
                ReadClassName(okButton),
                ButtonClassName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // BM_CLICK targets only this exact button. It neither focuses another
        // window nor leaves a keyboard event that can spill into the game.
        PostMessageW(okButton, BmClick, IntPtr.Zero, IntPtr.Zero);
    }

    private static string ReadWindowText(IntPtr window)
    {
        StringBuilder text = new(TextCapacity);
        IntPtr sent = SendMessageTimeoutW(
            window,
            WmGetText,
            (UIntPtr)TextCapacity,
            text,
            SmtoAbortIfHung,
            MessageTimeoutMilliseconds,
            out _);
        return sent == IntPtr.Zero ? string.Empty : text.ToString();
    }

    private static string ReadClassName(IntPtr window)
    {
        StringBuilder className = new(64);
        int length = GetClassNameW(window, className, className.Capacity);
        return length <= 0 ? string.Empty : className.ToString(0, length);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parent,
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetClassNameW(
        IntPtr window,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr dialog, int itemId);

    [DllImport(
        "user32.dll",
        EntryPoint = "SendMessageTimeoutW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr window,
        uint message,
        UIntPtr wParam,
        [Out] StringBuilder lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport(
        "user32.dll",
        EntryPoint = "PostMessageW",
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);
}
