using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Ghmr.Controller.Diagnostics;

namespace Ghmr.Controller.Ui;

internal static class TransitionMediaCatalog
{
    private static readonly string[] ImageFileNames =
    [
        "01-every-gta-different.jpg",
        "02-rarest.jpg",
        "03-cheats.jpg",
        "04-softlock.jpg",
        "05-logic.jpg",
        "06-best-gun.jpg"
    ];

    private static string MediaDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "media",
        "transitions");

    public static IEnumerable<string> ImagePaths => ImageFileNames.Select(
        fileName => Path.Combine(MediaDirectory, fileName));

    public static string AudioPath => Path.Combine(
        MediaDirectory,
        "transition-theme.mp3");
}

internal sealed class TransitionBackdropPanel : Panel
{
    private const int HoldTicks = 46;
    private const int FadeTicks = 14;
    private static int _startSequence = -1;

    private readonly List<Image> _images = [];
    private int _currentIndex;
    private int _nextIndex;
    private int _phaseTicks;
    private float _blend;

    public TransitionBackdropPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;

        foreach (string path in TransitionMediaCatalog.ImagePaths)
        {
            Image? image = LoadUnlocked(path);
            if (image is not null)
            {
                _images.Add(image);
            }
        }

        if (_images.Count > 0)
        {
            int sequence = Interlocked.Increment(ref _startSequence) & int.MaxValue;
            _currentIndex = sequence % _images.Count;
            _nextIndex = (_currentIndex + 1) % _images.Count;
        }
    }

    public void Advance()
    {
        if (_images.Count < 2)
        {
            return;
        }

        _phaseTicks++;
        if (_phaseTicks <= HoldTicks)
        {
            return;
        }

        _blend = Math.Clamp(
            (_phaseTicks - HoldTicks) / (float)FadeTicks,
            0F,
            1F);
        if (_blend >= 1F)
        {
            _currentIndex = _nextIndex;
            _nextIndex = (_nextIndex + 1) % _images.Count;
            _phaseTicks = 0;
            _blend = 0F;
        }

        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        Graphics graphics = eventArgs.Graphics;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (LinearGradientBrush fallback = new(
                   ClientRectangle,
                   Color.FromArgb(16, 22, 34),
                   Color.FromArgb(7, 9, 14),
                   LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(fallback, ClientRectangle);
        }

        if (_images.Count > 0)
        {
            DrawContained(graphics, _images[_currentIndex], 1F);
            if (_images.Count > 1 && _blend > 0F)
            {
                DrawContained(graphics, _images[_nextIndex], _blend);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Image image in _images)
            {
                image.Dispose();
            }
            _images.Clear();
        }

        base.Dispose(disposing);
    }

    private void DrawContained(Graphics graphics, Image image, float opacity)
    {
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
        {
            return;
        }

        Rectangle destination = GetContainedDestination(image, ClientRectangle);
        using ImageAttributes attributes = new();
        ColorMatrix matrix = new() { Matrix33 = opacity };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        graphics.DrawImage(
            image,
            destination,
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private static Rectangle GetContainedDestination(
        Image image,
        Rectangle available)
    {
        double scale = Math.Min(
            available.Width / (double)image.Width,
            available.Height / (double)image.Height);
        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        return new Rectangle(
            available.X + (available.Width - width) / 2,
            available.Y + (available.Height - height) / 2,
            width,
            height);
    }

    private static Image? LoadUnlocked(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using Image source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (Exception)
        {
            // Media is decorative. A missing or unreadable image must never
            // interrupt the handoff or prevent the controller from starting.
            return null;
        }
    }
}

internal sealed class TransitionAudioPlayer : IDisposable
{
    private const int PlaybackVolume = 270;
    private static int _aliasSequence;
    private static int _segmentSequence = -1;

    private readonly string _alias = $"ghmrtransition{Interlocked.Increment(ref _aliasSequence)}";
    private readonly bool _enabled;
    private readonly ControllerDiagnosticLog? _log;
    private bool _opened;
    private bool _disposed;

    public TransitionAudioPlayer(bool enabled, ControllerDiagnosticLog? log = null)
    {
        _enabled = enabled;
        _log = log;
    }

    public void Play()
    {
        if (!_enabled || _disposed || _opened || !OperatingSystem.IsWindows())
        {
            return;
        }

        string path = TransitionMediaCatalog.AudioPath;
        if (!File.Exists(path) || path.Contains('"'))
        {
            _log?.Warning("Audio", "Transition audio file is missing or its path is unsupported.");
            return;
        }

        uint result = Send($"open \"{path}\" type mpegvideo alias {_alias}");
        if (result != 0)
        {
            return;
        }

        _opened = true;
        Send($"set {_alias} time format milliseconds");
        Send($"setaudio {_alias} volume to {PlaybackVolume}");

        // Start each handoff at a different part of the five-minute track so
        // repeated cross-game transitions do not replay the same intro.
        int segment = Interlocked.Increment(ref _segmentSequence) % 5;
        int startMilliseconds = Math.Max(0, segment) * 45_000;
        Send($"play {_alias} from {startMilliseconds}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        if (_opened)
        {
            Send($"stop {_alias}");
            Send($"close {_alias}");
            _opened = false;
        }
    }

    private uint Send(string command)
    {
        string operation = command.Split(' ', 2)[0];
        _log?.Info("Audio", $"MCI operation beginning; operation={operation}; alias={_alias}");
        uint result = MciSendString(command, null, 0, IntPtr.Zero);
        if (result == 0)
            _log?.Info("Audio", $"MCI operation returned; operation={operation}; alias={_alias}; result=0");
        else
            _log?.Warning("Audio", $"MCI operation returned an error; operation={operation}; " +
                $"alias={_alias}; result={result}");
        return result;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern uint MciSendString(
        string command,
        string? returnValue,
        int returnLength,
        IntPtr callback);
}
