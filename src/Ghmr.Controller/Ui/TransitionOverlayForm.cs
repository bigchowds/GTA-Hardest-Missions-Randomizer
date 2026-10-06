using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using Ghmr.Controller.Diagnostics;

namespace Ghmr.Controller.Ui;

internal sealed class TransitionOverlayForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private static readonly Color GoldColor = Color.FromArgb(240, 196, 73);
    private static readonly Color CyanColor = Color.FromArgb(55, 195, 188);
    private static readonly Color SecondaryTextColor = Color.FromArgb(174, 184, 198);

    private readonly Label _gameLabel = new();
    private readonly Label _missionLabel = new();
    private readonly Label _positionLabel = new();
    private readonly Label _detailLabel = new();
    private readonly HandoffStagePanel[] _stages =
    [
        new("01", "CLOSING CURRENT GAME"),
        new("02", "LAUNCHING NEXT GAME"),
        new("03", "CONNECTING BRIDGE")
    ];
    private readonly TransitionPulseBar _pulseBar = new();
    private readonly TransitionBackdropPanel _backdrop = new();
    private readonly TransitionAudioPlayer _audioPlayer;
    private readonly System.Windows.Forms.Timer _animationTimer = new()
    {
        Interval = 80
    };
    private readonly System.Windows.Forms.Timer _audioSafetyTimer = new()
    {
        Interval = 60000
    };
    private readonly System.Windows.Forms.Timer _visibilityTimer = new() { Interval = 500 };
    private readonly ControllerDiagnosticLog? _log;
    private bool _yieldingToDialog;
    private bool _placementFailureLogged;

    public TransitionOverlayForm(Screen screen, bool playMusic, ControllerDiagnosticLog? log = null)
    {
        _log = log;
        _audioPlayer = new TransitionAudioPlayer(playMusic, log);
        Text = "GHMR Transition";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.FromArgb(8, 11, 17);
        ForeColor = Color.White;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        _animationTimer.Tick += (_, _) =>
        {
            _pulseBar.Advance();
            _backdrop.Advance();
        };
        _audioSafetyTimer.Tick += (_, _) =>
        {
            log?.Warning("Audio", "Music safety timeout reached after 60 seconds.");
            StopMusic();
        };
        _visibilityTimer.Tick += (_, _) => UpdateVisibility();
        Shown += (_, _) =>
        {
            UpdateVisibility();
            _visibilityTimer.Start();
            _animationTimer.Start();
            _audioPlayer.Play();
            _audioSafetyTimer.Start();
        };
        FormClosed += (_, _) =>
        {
            _visibilityTimer.Stop();
            _animationTimer.Stop();
            _audioSafetyTimer.Stop();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _visibilityTimer.Stop();
            _visibilityTimer.Dispose();
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _audioSafetyTimer.Stop();
            _audioSafetyTimer.Dispose();
            _audioPlayer.Dispose();
        }

        base.Dispose(disposing);
    }

    public void ApplyState(TransitionDisplayEventArgs state)
    {
        _gameLabel.Text = state.Game;
        _missionLabel.Text = state.Mission;
        _positionLabel.Text = $"MISSION {state.MissionNumber} OF {state.MissionCount}";
        _detailLabel.Text = state.Detail;

        int activeStage = state.Stage switch
        {
            TransitionDisplayStage.Closing => 0,
            TransitionDisplayStage.Launching => 1,
            TransitionDisplayStage.Connecting => 2,
            _ => 0
        };
        for (int index = 0; index < _stages.Length; index++)
        {
            _stages[index].State = index < activeStage
                ? HandoffStageState.Complete
                : index == activeStage
                    ? HandoffStageState.Active
                    : HandoffStageState.Pending;
        }

        UpdateVisibility();
    }

    public void StopMusic()
    {
        _audioSafetyTimer.Stop();
        _audioPlayer.Stop();
    }

    private void UpdateVisibility()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        nint foreground = GetForegroundWindow();
        bool dialog = ForegroundPromptOrController(foreground);
        if (_yieldingToDialog != dialog)
        {
            _yieldingToDialog = dialog;
            _log?.Info("Transition", dialog
                ? "Overlay yielding to a foreground dialog or controller window on the gameplay screen."
                : "Overlay visibility restored without activating the controller.");
        }

        // Keep the ownerless overlay above launcher windows in every handoff
        // stage. No activation or input-thread attachment is needed. MainForm
        // closes it, and its timers, as soon as the destination window exists.
        // A modal prompt stays accessible and is never dismissed or clicked.
        TopMost = !dialog;
        bool positioned = SetWindowPos(Handle, dialog ? foreground : new nint(-1),
            0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
        if (!positioned && !_placementFailureLogged)
            _log?.Warning("Transition", $"Non-activating overlay placement failed; nativeError={Marshal.GetLastWin32Error()}");
        _placementFailureLogged = !positioned;
    }

    private bool ForegroundPromptOrController(nint window)
    {
        if (window == nint.Zero || window == Handle || !IsWindowVisible(window)) return false;
        StringBuilder className = new(256);
        _ = GetClassName(window, className, className.Capacity);
        nint owner = GetWindow(window, 4); // GW_OWNER
        _ = GetWindowThreadProcessId(window, out uint processId);
        bool modal = className.ToString() == "#32770" ||
            (owner != nint.Zero && !IsWindowEnabled(owner)) ||
            processId == (uint)Environment.ProcessId;
        // A user who restores GHMR on a single screen must still be able to
        // use Stop Run or Options. A controller on another screen does not
        // affect the transition monitor's window order.
        return modal && GetWindowRect(window, out NativeRectangle rectangle) &&
            Bounds.IntersectsWith(Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int length);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

    private void BuildLayout()
    {
        _backdrop.Dock = DockStyle.Fill;
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 11, 17),
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 216F));

        Panel footerHost = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 11, 17),
            Margin = Padding.Empty,
            Padding = new Padding(28, 8, 28, 10)
        };

        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(17, 22, 30),
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22, 10, 22, 10),
            Margin = Padding.Empty
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 6F));
        content.Paint += (_, eventArgs) =>
        {
            using Pen border = new(Color.FromArgb(72, 83, 101), 1F);
            using SolidBrush accent = new(CyanColor);
            eventArgs.Graphics.DrawRectangle(
                border,
                0,
                0,
                Math.Max(0, content.Width - 1),
                Math.Max(0, content.Height - 1));
            eventArgs.Graphics.FillRectangle(accent, 0, 0, 5, content.Height);
        };

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

        Label heading = new()
        {
            Text = "GHMR  /  CROSS-GAME HANDOFF",
            Dock = DockStyle.Fill,
            ForeColor = CyanColor,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(heading, 0, 0);

        _positionLabel.Dock = DockStyle.Fill;
        _positionLabel.ForeColor = GoldColor;
        _positionLabel.Font = new Font(
            "Segoe UI Semibold",
            9F,
            FontStyle.Bold,
            GraphicsUnit.Point);
        _positionLabel.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(_positionLabel, 1, 0);
        content.Controls.Add(header, 0, 0);

        TableLayoutPanel statusLine = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        statusLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        statusLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));

        _gameLabel.Dock = DockStyle.Fill;
        _gameLabel.ForeColor = SecondaryTextColor;
        _gameLabel.Font = new Font(
            "Segoe UI Semibold",
            11F,
            FontStyle.Bold,
            GraphicsUnit.Point);
        _gameLabel.TextAlign = ContentAlignment.MiddleRight;
        _gameLabel.AutoEllipsis = true;

        _missionLabel.Dock = DockStyle.Fill;
        _missionLabel.ForeColor = Color.White;
        _missionLabel.Font = new Font(
            "Segoe UI Semibold",
            20F,
            FontStyle.Bold,
            GraphicsUnit.Point);
        _missionLabel.TextAlign = ContentAlignment.MiddleLeft;
        _missionLabel.AutoEllipsis = true;
        statusLine.Controls.Add(_missionLabel, 0, 0);
        statusLine.Controls.Add(_gameLabel, 1, 0);
        content.Controls.Add(statusLine, 0, 1);

        TableLayoutPanel stages = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        for (int index = 0; index < 3; index++)
        {
            stages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
            _stages[index].Dock = DockStyle.Fill;
            _stages[index].Margin = new Padding(
                index == 0 ? 0 : 5,
                5,
                index == 2 ? 0 : 5,
                5);
            stages.Controls.Add(_stages[index], index, 0);
        }
        content.Controls.Add(stages, 0, 2);

        TableLayoutPanel detailLine = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        detailLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        detailLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));

        _detailLabel.Dock = DockStyle.Fill;
        _detailLabel.ForeColor = SecondaryTextColor;
        _detailLabel.Font = new Font(
            "Segoe UI",
            10F,
            FontStyle.Regular,
            GraphicsUnit.Point);
        _detailLabel.TextAlign = ContentAlignment.MiddleLeft;
        _detailLabel.AutoEllipsis = true;
        detailLine.Controls.Add(_detailLabel, 0, 0);

        Label timerLabel = new()
        {
            Text = "GAMEPLAY TIMER PAUSED DURING HANDOFF",
            Dock = DockStyle.Fill,
            ForeColor = CyanColor,
            Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true
        };
        detailLine.Controls.Add(timerLabel, 1, 0);
        content.Controls.Add(detailLine, 0, 3);

        _pulseBar.Dock = DockStyle.Fill;
        _pulseBar.Margin = Padding.Empty;
        content.Controls.Add(_pulseBar, 0, 4);

        footerHost.Controls.Add(content);
        root.Controls.Add(_backdrop, 0, 0);
        root.Controls.Add(footerHost, 0, 1);
        Controls.Add(root);
    }
}

internal enum HandoffStageState
{
    Pending,
    Active,
    Complete
}

internal sealed class HandoffStagePanel : Panel
{
    private static readonly Font NumberFont = new(
        "Segoe UI Semibold",
        12F,
        FontStyle.Bold,
        GraphicsUnit.Point);
    private static readonly Font TitleFont = new(
        "Segoe UI Semibold",
        8.5F,
        FontStyle.Bold,
        GraphicsUnit.Point);

    private readonly string _number;
    private readonly string _title;
    private HandoffStageState _state;

    public HandoffStagePanel(string number, string title)
    {
        _number = number;
        _title = title;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public HandoffStageState State
    {
        get => _state;
        set
        {
            _state = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color accent = _state switch
        {
            HandoffStageState.Active => Color.FromArgb(240, 196, 73),
            HandoffStageState.Complete => Color.FromArgb(55, 195, 188),
            _ => Color.FromArgb(83, 93, 108)
        };
        Color text = _state == HandoffStageState.Pending
            ? Color.FromArgb(126, 136, 151)
            : Color.White;

        using SolidBrush background = new(Color.FromArgb(35, 41, 52));
        using SolidBrush accentBrush = new(accent);
        using Pen border = new(accent, _state == HandoffStageState.Active ? 2F : 1F);
        graphics.FillRectangle(background, ClientRectangle);
        graphics.FillRectangle(accentBrush, 0, 0, 4, Height);
        graphics.DrawRectangle(
            border,
            0,
            0,
            Math.Max(0, Width - 1),
            Math.Max(0, Height - 1));

        Rectangle numberBounds = new(17, 0, 36, Height);
        Rectangle titleBounds = new(58, 0, Math.Max(0, Width - 70), Height);
        TextRenderer.DrawText(
            graphics,
            _state == HandoffStageState.Complete ? "✓" : _number,
            NumberFont,
            numberBounds,
            accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(
            graphics,
            _title,
            TitleFont,
            titleBounds,
            text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}

internal sealed class TransitionPulseBar : Control
{
    private int _position;

    public TransitionPulseBar()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public void Advance()
    {
        _position = (_position + 12) % Math.Max(1, Width + 180);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush track = new(Color.FromArgb(55, 64, 78));
        using LinearGradientBrush pulse = new(
            new Rectangle(_position - 180, 0, 180, Math.Max(1, Height)),
            Color.FromArgb(55, 195, 188),
            Color.FromArgb(240, 196, 73),
            LinearGradientMode.Horizontal);
        int lineY = Math.Max(0, Height / 2 - 2);
        graphics.FillRectangle(track, 0, lineY, Width, 3);
        graphics.FillRectangle(pulse, _position - 180, lineY, 180, 3);
    }
}
