using Ghmr.Controller.Input;
using Ghmr.Controller.Installation;
using Ghmr.Controller.Launch;
using Ghmr.Core;
using Ghmr.Core.Run;
using Ghmr.Controller.Diagnostics;

namespace Ghmr.Controller.Ui;

public sealed class MainForm : Form
{
    private static readonly string[] FiveGameIds =
        ["sade", "gta3de", "vcde", "gta4", "gtav_enhanced"];
    private static readonly Color WindowColor = Color.FromArgb(10, 13, 19);
    private static readonly Color PanelColor = Color.FromArgb(25, 30, 39);
    private static readonly Color PanelBorderColor = Color.FromArgb(54, 63, 77);
    private static readonly Color GoldColor = Color.FromArgb(240, 196, 73);
    private static readonly Color CyanColor = Color.FromArgb(55, 195, 188);
    private static readonly Color MagentaColor = Color.FromArgb(218, 80, 145);
    private static readonly Color DangerColor = Color.FromArgb(220, 82, 88);
    private static readonly Color SecondaryTextColor = Color.FromArgb(174, 184, 198);

    private readonly ControllerCoordinator _coordinator;
    private readonly GameLaunchProfileStore _profiles;
    private readonly ControllerPreferencesStore _preferences;
    private readonly ControllerDiagnosticLog? _log;
    private readonly XInputGamepad _gamepad = new();
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _gamepadTimer = new() { Interval = 50 };
    private readonly List<Button> _gamepadButtons = [];
    private readonly ToolTip _toolTip = new()
    {
        InitialDelay = 250,
        ReshowDelay = 100,
        AutoPopDelay = 6000,
        ShowAlways = true
    };
    private readonly ContextMenuStrip _diagnosticsMenu = new();
    private readonly System.Windows.Forms.Timer _transitionSafetyTimer = new()
    {
        Interval = 120000
    };
    private readonly System.Windows.Forms.Timer _gameWindowFocusTimer = new()
    {
        Interval = 250
    };
    private TransitionOverlayForm? _transitionOverlay;
    private long _lastTransitionSequence;
    private int _gameWindowFocusAttempts;
    private string? _gameWindowFocusGame;
    private string? _gameplayScreenDeviceName;
    private bool _handoffPresentationActive;
    private readonly System.Windows.Forms.Timer _resumeTimer = new() { Interval = 1500 };
    private string? _resumeRun;
    private int _resumeIndex = -1;
    private DateTimeOffset _resumeDeadline;
    private bool _resumeBusy;
    private readonly System.Windows.Forms.Timer _passScreenTimer = new() { Interval = 600 };
    private readonly GtaVPassScreen _passScreen = new();
    private bool _passScreenBusy;
    private string? _passScreenContext;
    private string? _passScreenArmedContext;
    private int _passScreenFrames;
    private DateTimeOffset _lastPassScreenFrame;
    private bool _passScreenWarningLogged;

    private readonly Label _phaseValue = CreateValueLabel();
    private readonly Label _missionValue = CreateValueLabel(19F);
    private readonly Label _gameValue = CreateValueLabel();
    private readonly Label _progressValue = CreateValueLabel();
    private readonly Label _gameplayTimeValue = CreateValueLabel(25F, autoEllipsis: false);
    private readonly Label _realTimeValue = CreateValueLabel(18F, autoEllipsis: false);
    private readonly Label _failuresValue = CreateValueLabel();
    private readonly Label _bridgeStatusValue = CreateValueLabel();
    private readonly Label _controllerStatusValue = CreateValueLabel();
    private readonly Label _messageValue = CreateValueLabel(9.5F);
    private readonly ActionButton _startButton = new(
        "Start Run",
        "Five missions across all five games",
        ActionIcon.Play,
        GoldColor);
    private readonly ActionButton _chaosButton = new(
        "Chaos Mode",
        "Weapons, vehicles and NPC modifiers",
        ActionIcon.Chaos,
        MagentaColor);
    private readonly ActionButton _configureButton = new(
        "Setup Game Bridges",
        "Configure games and install adapters",
        ActionIcon.Bridge,
        CyanColor);
    private readonly ActionButton _stopButton = new(
        "Stop Run",
        "End the current attempt safely",
        ActionIcon.Stop,
        DangerColor);
    private readonly Button _diagnosticsButton = CreateCompactButton("DIAGNOSTICS  ▾");
    private readonly Button _optionsButton = CreateCompactButton("OPTIONS");

    public MainForm(
        ControllerCoordinator coordinator,
        GameLaunchProfileStore profiles,
        ControllerPreferencesStore preferences,
        ControllerDiagnosticLog? log = null)
    {
        _coordinator = coordinator;
        _profiles = profiles;
        _preferences = preferences;
        _log = log;
        Text = "GHMR - Five-Mission Beta";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 740);
        ClientSize = new Size(940, 780);
        BackColor = WindowColor;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;

        BuildLayout();
        BuildDiagnosticsMenu();

        _gamepadButtons.Add(_startButton);
        _gamepadButtons.Add(_chaosButton);
        _gamepadButtons.Add(_configureButton);
        _gamepadButtons.Add(_stopButton);
        _gamepadButtons.Add(_optionsButton);
        _gamepadButtons.Add(_diagnosticsButton);
        _startButton.Click += async (_, _) => await BeginBetaRunAsync();
        _chaosButton.Click += (_, _) => ShowChaosComingSoon();
        _configureButton.Click += (_, _) => ConfigureGamePaths();
        _stopButton.Click += (_, _) => StopRun();
        _optionsButton.Click += (_, _) => ShowOptions();
        _diagnosticsButton.Click += (_, _) => ShowDiagnosticsMenu();
        _toolTip.SetToolTip(
            _chaosButton,
            "Coming soon - curated weapon, vehicle and NPC randomization after the five-mission Beta.");
        _toolTip.SetToolTip(
            _diagnosticsButton,
            "Open repeatable test routes for individual games.");

        _coordinator.StateChanged += (_, _) => ScheduleUiRefresh();
        _coordinator.StatusChanged += (_, _) => ScheduleUiRefresh();
        _coordinator.TransitionChanged += (_, args) =>
            ScheduleTransitionUpdate(args);
        _coordinator.TransitionAudioStopRequested += (_, _) =>
            ScheduleTransitionAudioStop();
        _displayTimer.Tick += (_, _) => RefreshUi();
        _gamepadTimer.Tick += (_, _) => PollGamepad();
        _transitionSafetyTimer.Tick += (_, _) =>
        {
            _log?.Warning("Transition", "Overlay safety timeout reached after 120 seconds.");
            CloseTransitionOverlay();
        };
        _gameWindowFocusTimer.Tick += (_, _) => TryFocusGameWindow();
        _resumeTimer.Tick += async (_, _) => await TryViceCityResumeAsync();
        _passScreenTimer.Tick += async (_, _) => await TryGtaVPassScreenAsync();
        Shown += (_, _) =>
        {
            _coordinator.Start();
            _displayTimer.Start();
            _gamepadTimer.Start();
            _passScreenTimer.Start();
            _startButton.Focus();
            RefreshUi();
        };
        FormClosed += (_, _) =>
        {
            _log?.Info("UI", "Controller window closing.");
            _displayTimer.Stop();
            _gamepadTimer.Stop();
            _transitionSafetyTimer.Stop();
            _gameWindowFocusTimer.Stop();
            _resumeTimer.Stop();
            _passScreenTimer.Stop();
            CloseTransitionOverlay();
            _coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };
    }

    private void BuildLayout()
    {
        GtaBackdropPanel backdrop = new()
        {
            Dock = DockStyle.Fill
        };
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 20, 26, 18),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 166F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182F));

        TableLayoutPanel titleBlock = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 67F));
        titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));

        Label heading = new()
        {
            Text = "GTA HARDEST MISSIONS RANDOMIZER",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        };
        Label tagline = new()
        {
            Text = "FIVE GAMES  /  ONE RUN  /  NO REROLLS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = CyanColor,
            TextAlign = ContentAlignment.TopLeft
        };
        titleBlock.Controls.Add(heading, 0, 0);
        titleBlock.Controls.Add(tagline, 0, 1);
        header.Controls.Add(titleBlock, 0, 0);

        Panel betaBadge = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(38, 32, 24),
            Margin = new Padding(14, 18, 0, 18),
            Padding = new Padding(2)
        };
        betaBadge.Paint += (_, eventArgs) =>
        {
            using Pen border = new(GoldColor, 1F);
            eventArgs.Graphics.DrawRectangle(
                border,
                0,
                0,
                Math.Max(0, betaBadge.Width - 1),
                Math.Max(0, betaBadge.Height - 1));
        };
        betaBadge.Controls.Add(new Label
        {
            Text = "5-MISSION BETA",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = GoldColor,
            TextAlign = ContentAlignment.MiddleCenter
        });
        header.Controls.Add(betaBadge, 1, 0);
        root.Controls.Add(header, 0, 0);

        TableLayoutPanel missionPanel = CreatePanel(columns: 2, rows: 5, CyanColor);
        AddRow(missionPanel, 0, "Run state", _phaseValue);
        AddRow(missionPanel, 1, "Current mission", _missionValue);
        AddRow(missionPanel, 2, "Game", _gameValue);
        AddRow(missionPanel, 3, "Completed", _progressValue);
        AddRow(missionPanel, 4, "Failures", _failuresValue);
        root.Controls.Add(missionPanel, 0, 1);

        TableLayoutPanel timerPanel = CreateTimerPanel();
        root.Controls.Add(timerPanel, 0, 2);

        TableLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 8, 0, 6),
            Margin = Padding.Empty
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        AddActionButton(buttons, _startButton, column: 0, row: 0);
        AddActionButton(buttons, _chaosButton, column: 1, row: 0);
        AddActionButton(buttons, _configureButton, column: 0, row: 1);
        AddActionButton(buttons, _stopButton, column: 1, row: 1);
        root.Controls.Add(buttons, 0, 3);

        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142F));
        _messageValue.Dock = DockStyle.Fill;
        _messageValue.ForeColor = SecondaryTextColor;
        _messageValue.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(_messageValue, 0, 0);
        _optionsButton.Dock = DockStyle.Fill;
        _optionsButton.Margin = new Padding(8, 5, 0, 5);
        footer.Controls.Add(_optionsButton, 1, 0);
        _diagnosticsButton.Dock = DockStyle.Fill;
        _diagnosticsButton.Margin = new Padding(8, 5, 0, 5);
        footer.Controls.Add(_diagnosticsButton, 2, 0);
        root.Controls.Add(footer, 0, 4);

        backdrop.Controls.Add(root);
        Controls.Add(backdrop);
    }

    private async Task BeginBetaRunAsync()
    {
        try
        {
            string[] missions = _preferences.Current.FixedTestOrderEnabled
                ? BetaRunChoices.ValidateFixedOrder(_preferences.Current.FixedMissionOrder)
                : BetaRunChoices.MissionIds.ToArray();
            string[] games = missions.Select(MissionGame).Distinct(StringComparer.Ordinal).ToArray();
            if (!EnsureCompatibleBridges(games))
                return;

            _log?.Info("Run", _preferences.Current.FixedTestOrderEnabled
                ? "Start Run selected: fixed test order."
                : "Start Run selected: random five-mission order.");
            Task<RunSnapshot> launch = _preferences.Current.FixedTestOrderEnabled
                ? _coordinator.BeginValidationRunAsync(missions)
                : _coordinator.BeginBetaRunAsync(missions);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start run",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static string MissionGame(string id) => id switch
    {
        "sa.wrong_side_of_the_tracks" => "sade",
        "gta3.espresso_2_go" => "gta3de",
        "vc.demolition_man" => "vcde",
        "gta4.three_leaf_clover" => "gta4",
        "gtav.derailed" => "gtav_enhanced",
        _ => throw new ArgumentException("Unknown beta mission.", nameof(id))
    };

    private async Task BeginFixedHandoffTestAsync()
    {
        try
        {
            if (!EnsureCompatibleBridges(FiveGameIds))
                return;

            Task<RunSnapshot> launch = _coordinator.BeginValidationRunAsync(
            [
                "sa.wrong_side_of_the_tracks",
                "gta3.espresso_2_go",
                "vc.demolition_man",
                "gta4.three_leaf_clover",
                "gtav.derailed"
            ]);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start fixed diagnostic run",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task BeginGta3TestAsync()
    {
        try
        {
            if (!EnsureCompatibleBridges(["gta3de"]))
                return;

            Task<RunSnapshot> launch = _coordinator.BeginValidationRunAsync(
            [
                "gta3.espresso_2_go"
            ]);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start GTA III test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task BeginSanAndreasTestAsync()
    {
        try
        {
            if (!EnsureCompatibleBridges(["sade"]))
                return;

            Task<RunSnapshot> launch = _coordinator.BeginValidationRunAsync(
            [
                "sa.wrong_side_of_the_tracks"
            ]);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start San Andreas test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task BeginViceCityTestAsync()
    {
        try
        {
            if (!EnsureCompatibleBridges(["vcde"]))
                return;

            Task<RunSnapshot> launch = _coordinator.BeginValidationRunAsync(
            [
                "vc.demolition_man"
            ]);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start Vice City test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task BeginGta4TestAsync()
    {
        try
        {
            if (!EnsureCompatibleBridges(["gta4"]))
                return;

            Task<RunSnapshot> launch = _coordinator.BeginValidationRunAsync(
            [
                "gta4.three_leaf_clover"
            ]);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start GTA IV test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task BeginGta5TestAsync()
    {
        try
        {
            if (!EnsureCompatibleBridges(["gtav_enhanced"]))
                return;

            Task<RunSnapshot> launch = _coordinator.BeginValidationRunAsync(
            [
                "gtav.derailed"
            ]);
            BeginGameWindowFocus();
            await launch;
            RefreshUi();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RestoreControllerWindow();
            MessageBox.Show(
                this,
                exception.Message,
                "Cannot start GTA V test",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void StopRun()
    {
        RunSnapshot snapshot = _coordinator.Snapshot;
        if (snapshot.Phase == RunPhase.Idle || snapshot.IsTerminal)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            "Stop this run? It cannot be resumed or submitted as a completed result.",
            "Stop run",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result == DialogResult.Yes)
        {
            _coordinator.AbortRun("Player stopped the run from the controller UI.");
            RefreshUi();
        }
    }

    private void ShowChaosComingSoon()
    {
        MessageBox.Show(
            this,
            "Chaos Mode is coming soon. It will add curated weapon, vehicle and NPC modifiers after the five-mission Beta is stable.",
            "Chaos Mode - Coming Soon",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void BuildDiagnosticsMenu()
    {
        _diagnosticsMenu.BackColor = PanelColor;
        _diagnosticsMenu.ForeColor = Color.White;
        _diagnosticsMenu.Font = new Font(
            "Segoe UI",
            9.5F,
            FontStyle.Regular,
            GraphicsUnit.Point);
        _diagnosticsMenu.Renderer = new ToolStripProfessionalRenderer(
            new GtaMenuColorTable());

        _diagnosticsMenu.Items.Add(
            "Test all five (fixed order)",
            null,
            async (_, _) => await BeginFixedHandoffTestAsync());
        _diagnosticsMenu.Items.Add(new ToolStripSeparator());
        _diagnosticsMenu.Items.Add(
            "Test San Andreas only",
            null,
            async (_, _) => await BeginSanAndreasTestAsync());
        _diagnosticsMenu.Items.Add(
            "Test GTA III only",
            null,
            async (_, _) => await BeginGta3TestAsync());
        _diagnosticsMenu.Items.Add(
            "Test Vice City only",
            null,
            async (_, _) => await BeginViceCityTestAsync());
        _diagnosticsMenu.Items.Add(
            "Test GTA IV only",
            null,
            async (_, _) => await BeginGta4TestAsync());
        _diagnosticsMenu.Items.Add(
            "Test GTA V only",
            null,
            async (_, _) => await BeginGta5TestAsync());
    }

    private void ShowDiagnosticsMenu()
    {
        _diagnosticsMenu.Show(
            _diagnosticsButton,
            new Point(0, _diagnosticsButton.Height));
    }

    private void ShowOptions()
    {
        using OptionsForm options = new(_preferences, _log);
        if (options.ShowDialog(this) == DialogResult.OK)
        {
            _messageValue.Text = _preferences.Current.TransitionMusicEnabled
                ? "Transition music enabled."
                : "Transition music muted.";
        }
    }

    private void ConfigureGamePaths(string? initialGame = null)
    {
        using GameSetupForm setup = new(_profiles, _log, initialGame);
        setup.ShowDialog(this);
        _messageValue.Text = "Game launch settings updated locally.";
    }

    private bool EnsureCompatibleBridges(IEnumerable<string> games)
    {
        List<BridgeCompatibilityResult> problems = games
            .Distinct(StringComparer.Ordinal)
            .Select(game => BridgeCompatibility.Inspect(_profiles.Find(game), game))
            .Where(result => !result.IsCompatible)
            .ToList();
        if (problems.Count == 0)
            return true;

        foreach (BridgeCompatibilityResult problem in problems)
        {
            _log?.Warning("Setup", $"Run blocked by bridge preflight; game={problem.Game}; " +
                $"installed={problem.InstalledVersion}; required={problem.RequiredVersion}; " +
                $"problem={problem.Problem}; script={problem.ScriptPath}");
        }

        string details = string.Join(
            "\n",
            problems.Select(problem =>
                $"{ControllerCoordinator.DisplayGame(problem.Game)}: " +
                $"{problem.Problem} Required v{problem.RequiredVersion}."));
        MessageBox.Show(
            this,
            "GHMR stopped before launching a mission because these game bridges " +
            "are missing or outdated:\n\n" + details +
            "\n\nSetup will open on the first affected game. Select " +
            "Install / Repair Bridge, wait for the verified-version message, " +
            "then start the test again.",
            "Bridge repair required",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        ConfigureGamePaths(problems[0].Game);
        return false;
    }

    private void PollGamepad()
    {
        GamepadFrame frame = _gamepad.Poll();
        _controllerStatusValue.Text = frame.Connected
            ? $"Connected (XInput slot {frame.Slot + 1})"
            : "Not detected";

        if (!ContainsFocus || !frame.Connected)
        {
            return;
        }

        if (frame.NavigateUpPressed)
        {
            MoveButtonFocus(-1);
        }
        else if (frame.NavigateDownPressed)
        {
            MoveButtonFocus(1);
        }

        if (frame.BackPressed && _stopButton.Enabled)
        {
            _stopButton.Focus();
        }

        if (frame.ConfirmPressed && ActiveControl is Button button && button.Enabled)
        {
            button.PerformClick();
        }
    }

    private void MoveButtonFocus(int offset)
    {
        List<Button> enabled = _gamepadButtons.Where(button => button.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return;
        }

        int current = enabled.FindIndex(button => ReferenceEquals(button, ActiveControl));
        int next = current < 0
            ? 0
            : (current + offset + enabled.Count) % enabled.Count;
        enabled[next].Focus();
    }

    private void RefreshUi()
    {
        RunSnapshot snapshot = _coordinator.Snapshot;
        MissionDefinition? mission = _coordinator.CurrentMission;
        _toolTip.SetToolTip(_startButton, _preferences.Current.FixedTestOrderEnabled
            ? "Start the selected fixed diagnostic route. Change this in Options."
            : "Start all five missions in random order, with a random trilogy game first.");
        string subtitle = _preferences.Current.FixedTestOrderEnabled
            ? "Fixed test order — selected missions" : "Random order — five missions";
        if (_startButton.Subtitle != subtitle)
        {
            _startButton.Subtitle = subtitle;
            _startButton.Invalidate();
        }

        _phaseValue.Text = snapshot.Phase switch
        {
            RunPhase.Finished => "Finished",
            RunPhase.Aborted => "Stopped",
            _ => snapshot.Phase.ToString()
        };
        _missionValue.Text = mission?.Title ?? "—";
        _gameValue.Text = mission is null
            ? "—"
            : ControllerCoordinator.DisplayGame(mission.Game);
        _progressValue.Text = snapshot.MissionOrder.Count == 0
            ? "0 / 5"
            : $"{snapshot.CompletedMissionIds.Count} / {snapshot.MissionOrder.Count}";
        _failuresValue.Text = snapshot.Failures.ToString();
        _gameplayTimeValue.Text = FormatDuration(snapshot.GameplayElapsedMilliseconds);
        _realTimeValue.Text = FormatDuration(snapshot.RealElapsedMilliseconds);
        _bridgeStatusValue.Text = snapshot.IsTerminal
            ? "Inactive"
            : snapshot.ActiveBridgeSessionId is null
            ? "Waiting"
            : "Verified session active";
        _messageValue.Text = _coordinator.Status +
            "   A: select  •  B: focus Stop";

        bool active = snapshot.Phase != RunPhase.Idle && !snapshot.IsTerminal;
        _startButton.Enabled = !active;
        _chaosButton.Enabled = !active;
        _configureButton.Enabled = !active;
        _stopButton.Enabled = active;
        // Support logs must remain accessible when a handoff is stuck.
        _optionsButton.Enabled = true;
        _diagnosticsButton.Enabled = !active;
    }

    private void ScheduleUiRefresh()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke((Action)RefreshUi);
    }

    private void ScheduleTransitionUpdate(TransitionDisplayEventArgs state)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        _log?.Info("Transition", $"UI update queued; sequence={state.Sequence}; " +
            $"visible={state.Visible}; stage={state.Stage}; game={state.Game}");
        BeginInvoke((Action)(() =>
        {
            try { ApplyTransitionUpdate(state); }
            catch (Exception exception)
            {
                _log?.Error("Transition", "Overlay UI update failed", exception);
                throw;
            }
        }));
    }

    private void ScheduleTransitionAudioStop()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            _log?.Info("Audio", "Music stop queued on the UI thread before mission start.");
            // Opening and closing the MCI alias must remain on the UI thread.
            // BeginInvoke returns immediately so bridge messages cannot stall.
            BeginInvoke((Action)(() => _transitionOverlay?.StopMusic()));
        }
        catch (InvalidOperationException exception)
        {
            _log?.Error("Audio", "Could not queue music stop while closing", exception);
            // The controller may be closing while a final bridge event arrives.
        }
    }

    private void ApplyTransitionUpdate(TransitionDisplayEventArgs state)
    {
        if (state.Sequence <= _lastTransitionSequence || IsDisposed)
        {
            return;
        }
        _lastTransitionSequence = state.Sequence;
        _log?.Info("Transition", $"Applying UI update; sequence={state.Sequence}; " +
            $"visible={state.Visible}; stage={state.Stage}; detail={state.Detail}");

        if (!state.Visible)
        {
            bool handoffWasVisible = _handoffPresentationActive;
            _handoffPresentationActive = false;
            CloseTransitionOverlay();
            RunSnapshot snapshot = _coordinator.Snapshot;
            if (!snapshot.IsTerminal && snapshot.Phase is RunPhase.Launching or
                RunPhase.Transitioning or RunPhase.Preparing or RunPhase.Running or RunPhase.Restarting)
            {
                // The bridge has confirmed player control. Give the game one
                // final foreground handoff after the overlay is removed.
                BeginGameWindowFocus();
            }
            else if (snapshot.IsTerminal || handoffWasVisible)
            {
                // A completed/aborted run or failed launch must never leave a
                // single-monitor user with only a minimized controller.
                RestoreControllerWindow();
            }
            return;
        }

        _handoffPresentationActive = true;

        _transitionOverlay ??= new TransitionOverlayForm(
            GetGameplayScreen(),
            _preferences.Current.TransitionMusicEnabled,
            _log);
        _transitionOverlay.ApplyState(state);
        if (!_transitionOverlay.Visible)
        {
            // The overlay is intentionally ownerless. Closing an owned form
            // activates its owner, which made GTA III pause and left the GHMR
            // controller in front of the game.
            _transitionOverlay.Show();
        }

        PrepareControllerForGameplay();

        _transitionSafetyTimer.Stop();
        if (state.Stage == TransitionDisplayStage.Connecting)
        {
            // The overlay remains visible without taking focus and yields to
            // foreground modal prompts. End it when the game's window appears,
            // with an emergency limit if that window never becomes available.
            _transitionSafetyTimer.Start();
            BeginGameWindowFocus();
        }
    }

    private void BeginGameWindowFocus()
    {
        MissionDefinition? mission = _coordinator.CurrentMission;
        if (mission is null)
        {
            return;
        }

        _gameWindowFocusGame = mission.Game;
        RunSnapshot snapshot = _coordinator.Snapshot;
        if (mission.Game == "vcde" && _preferences.Current.ViceCityAutoResumeEnabled &&
            (snapshot.Phase is RunPhase.Launching or RunPhase.Transitioning) &&
            (_resumeRun != snapshot.RunId || _resumeIndex != snapshot.CurrentIndex))
        {
            _resumeRun = snapshot.RunId;
            _resumeIndex = snapshot.CurrentIndex;
            _resumeDeadline = DateTimeOffset.UtcNow.AddSeconds(90);
            _resumeTimer.Start();
        }
        _log?.Info("Focus", $"Game-window focus search started; game={mission.Game}");
        _gameWindowFocusAttempts = 0;
        PrepareControllerForGameplay();
        _gameWindowFocusTimer.Start();
        TryFocusGameWindow();
    }

    private async Task TryViceCityResumeAsync()
    {
        if (_resumeBusy) return;
        bool StillWaiting()
        {
            RunSnapshot state = _coordinator.Snapshot;
            return !IsDisposed && _preferences.Current.ViceCityAutoResumeEnabled &&
                state.RunId == _resumeRun && state.CurrentIndex == _resumeIndex &&
                state.ActiveBridgeSessionId is null &&
                (state.Phase is RunPhase.Launching or RunPhase.Transitioning) &&
                _coordinator.CurrentMission?.Game == "vcde";
        }
        if (!StillWaiting() || DateTimeOffset.UtcNow >= _resumeDeadline)
        {
            _resumeTimer.Stop();
            return;
        }
        Ghmr.Core.Launch.GameLaunchProfile? profile = _profiles.Find("vcde");
        if (profile is null || !GameWindowFocus.TryFindMainWindow(profile, out nint window)) return;
        _resumeBusy = true;
        try
        {
            if (await ViceCityAutoResume.TrySelectAsync(window, StillWaiting, _log))
                _resumeTimer.Stop();
        }
        catch (Exception exception)
        {
            _resumeTimer.Stop();
            _log?.Warning("Startup", $"Automatic VC Resume unavailable; use manual Resume. {exception.GetType().Name}");
        }
        finally { _resumeBusy = false; }
    }

    private async Task TryGtaVPassScreenAsync()
    {
        if (_passScreenBusy || IsDisposed) return;
        RunSnapshot captured = _coordinator.Snapshot;
        string context = $"{captured.RunId}/{captured.CurrentIndex}/{captured.ActiveBridgeSessionId}/{captured.Failures}";
        if (context != _passScreenContext)
        {
            _passScreenContext = context;
            _passScreenFrames = 0;
            _passScreenWarningLogged = false;
        }
        bool StillCurrent()
        {
            RunSnapshot current = _coordinator.Snapshot;
            return !IsDisposed && current.Phase == RunPhase.Running &&
                current.RunId == captured.RunId && current.CurrentIndex == captured.CurrentIndex &&
                current.ActiveBridgeSessionId == captured.ActiveBridgeSessionId &&
                current.Failures == captured.Failures && _coordinator.CurrentMission?.Id == "gtav.derailed";
        }
        if (captured.ActiveBridgeSessionId is null || !StillCurrent())
        { _passScreenFrames = 0; return; }
        Ghmr.Core.Launch.GameLaunchProfile? profile = _profiles.Find("gtav_enhanced");
        if (profile is null || !GameWindowFocus.TryFindMainWindow(profile, out nint window))
        { _passScreenFrames = 0; return; }
        if (_passScreenArmedContext != context)
        {
            _passScreenArmedContext = context;
            _log?.Info("Completion", "GTA V Derailed result-screen watcher armed; waiting for foreground Mission Passed banner and title.");
        }
        _passScreenBusy = true;
        try
        {
            PassScreenResult result = await _passScreen.ReadAsync(window, StillCurrent);
            if (result != PassScreenResult.DerailedPassed || !StillCurrent())
            {
                _passScreenFrames = 0;
                if (result == PassScreenResult.OcrUnavailable && !_passScreenWarningLogged)
                {
                    _passScreenWarningLogged = true;
                    _log?.Warning("Completion", "GTA V pass banner matched but English Windows text recognition is unavailable; native bridge detection retained.");
                }
                return;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastPassScreenFrame > TimeSpan.FromSeconds(4)) _passScreenFrames = 0;
            _lastPassScreenFrame = now;
            if (++_passScreenFrames < 2)
            {
                _log?.Info("Completion", "Foreground GTA V Mission Passed banner and Derailed title matched; confirming a second frame.");
                return;
            }
            await _coordinator.ConfirmGtaVPassScreenAsync(captured.RunId, captured.CurrentIndex,
                captured.ActiveBridgeSessionId, captured.Failures);
            _passScreenFrames = 0;
        }
        catch (Exception exception)
        {
            _passScreenFrames = 0;
            if (!_passScreenWarningLogged && !IsDisposed)
            {
                _passScreenWarningLogged = true;
                _log?.Error("Completion", "GTA V result-screen recognition unavailable; native bridge detection retained", exception);
            }
        }
        finally { _passScreenBusy = false; }
    }

    private void TryFocusGameWindow()
    {
        if (IsDisposed || _gameWindowFocusGame is null)
        {
            _gameWindowFocusTimer.Stop();
            return;
        }

        // Match the existing two-minute launch/overlay safety window.
        if (++_gameWindowFocusAttempts > 480)
        {
            _log?.Warning("Focus", $"Game-window focus timed out; game={_gameWindowFocusGame}");
            _gameWindowFocusTimer.Stop();
            return;
        }

        Ghmr.Core.Launch.GameLaunchProfile? profile =
            _profiles.Find(_gameWindowFocusGame);
        if (profile is null ||
            !GameWindowFocus.TryFindMainWindow(profile, out nint gameWindow))
        {
            return;
        }

        Screen gameScreen = Screen.FromHandle(gameWindow);
        _gameplayScreenDeviceName = gameScreen.DeviceName;
        if (_transitionOverlay is not null &&
            !string.Equals(
                Screen.FromControl(_transitionOverlay).DeviceName,
                gameScreen.DeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            _transitionOverlay.Bounds = gameScreen.Bounds;
        }

        MoveControllerAwayFrom(gameScreen);
        RunSnapshot windowSnapshot = _coordinator.Snapshot;
        _coordinator.DestinationWindowDetected(
            _gameWindowFocusGame, windowSnapshot.RunId, windowSnapshot.CurrentIndex);
        _handoffPresentationActive = false;
        CloseTransitionOverlay();
        if (!GameWindowFocus.TryActivate(gameWindow))
        {
            if (_gameWindowFocusAttempts % 40 == 0)
                _log?.Warning("Focus", $"Game window found but foreground activation failed; " +
                    $"game={_gameWindowFocusGame}; attempt={_gameWindowFocusAttempts}");
            return;
        }

        _log?.Info("Focus", $"Game window activated; game={_gameWindowFocusGame}; " +
            $"attempt={_gameWindowFocusAttempts}; screen={gameScreen.DeviceName}");
        _gameWindowFocusTimer.Stop();
    }

    private void PrepareControllerForGameplay()
    {
        if (Screen.AllScreens.Length == 1 && WindowState != FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Minimized;
        }
    }

    private void MoveControllerAwayFrom(Screen gameScreen)
    {
        Screen[] screens = Screen.AllScreens;
        if (screens.Length == 1)
        {
            WindowState = FormWindowState.Minimized;
            return;
        }

        Screen controllerScreen = Screen.FromControl(this);
        if (WindowState != FormWindowState.Minimized &&
            !string.Equals(
                controllerScreen.DeviceName,
                gameScreen.DeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Screen? target = screens.FirstOrDefault(screen => !string.Equals(
            screen.DeviceName,
            gameScreen.DeviceName,
            StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            WindowState = FormWindowState.Minimized;
            return;
        }

        Rectangle area = target.WorkingArea;
        Size desired = WindowState == FormWindowState.Minimized &&
            !RestoreBounds.IsEmpty
                ? RestoreBounds.Size
                : Size;
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            area.Left + Math.Max(0, (area.Width - desired.Width) / 2),
            area.Top + Math.Max(0, (area.Height - desired.Height) / 2));
    }

    private Screen GetGameplayScreen()
    {
        if (_gameplayScreenDeviceName is not null)
        {
            Screen? remembered = Screen.AllScreens.FirstOrDefault(screen =>
                string.Equals(
                    screen.DeviceName,
                    _gameplayScreenDeviceName,
                    StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
            {
                return remembered;
            }
        }

        return Screen.PrimaryScreen ?? Screen.FromControl(this);
    }

    private void RestoreControllerWindow()
    {
        _gameWindowFocusTimer.Stop();
        _gameWindowFocusGame = null;
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
    }

    private void CloseTransitionOverlay()
    {
        _transitionSafetyTimer.Stop();
        if (_transitionOverlay is null)
        {
            return;
        }

        _log?.Info("Transition", "Closing overlay and disposing transition media.");
        _transitionOverlay.Close();
        _transitionOverlay.Dispose();
        _transitionOverlay = null;
        _log?.Info("Transition", "Overlay closed.");
    }

    private static string FormatDuration(long milliseconds)
    {
        TimeSpan value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}";
    }

    private TableLayoutPanel CreateTimerPanel()
    {
        TableLayoutPanel panel = CreatePanel(columns: 2, rows: 2, GoldColor);
        panel.Padding = new Padding(18, 14, 18, 14);
        panel.Margin = new Padding(0, 0, 0, 8);
        panel.ColumnStyles.Clear();
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43F));
        panel.RowStyles.Clear();
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        panel.Controls.Add(
            CreateMetricBlock("GAMEPLAY TIME", _gameplayTimeValue),
            0,
            0);
        panel.Controls.Add(
            CreateMetricBlock("REAL ELAPSED", _realTimeValue),
            1,
            0);
        panel.Controls.Add(
            CreateStatusBlock("BRIDGE", _bridgeStatusValue),
            0,
            1);
        panel.Controls.Add(
            CreateStatusBlock("CONTROLLER", _controllerStatusValue),
            1,
            1);
        return panel;
    }

    private static TableLayoutPanel CreateMetricBlock(string title, Label value)
    {
        TableLayoutPanel block = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0)
        };
        block.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
        block.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        block.Controls.Add(CreateCaptionLabel(title), 0, 0);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Margin = Padding.Empty;
        block.Controls.Add(value, 0, 1);
        return block;
    }

    private static TableLayoutPanel CreateStatusBlock(string title, Label value)
    {
        TableLayoutPanel block = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 12, 0)
        };
        block.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        block.Controls.Add(CreateCaptionLabel(title), 0, 0);
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Margin = Padding.Empty;
        value.Font = new Font(
            "Segoe UI Semibold",
            10F,
            FontStyle.Bold,
            GraphicsUnit.Point);
        block.Controls.Add(value, 1, 0);
        return block;
    }

    private static Label CreateCaptionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(
                "Segoe UI Semibold",
                8.5F,
                FontStyle.Bold,
                GraphicsUnit.Point)
        };
    }

    private static TableLayoutPanel CreatePanel(
        int columns,
        int rows,
        Color accentColor)
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            Padding = new Padding(22, 16, 18, 16),
            ColumnCount = columns,
            RowCount = rows,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int row = 0; row < rows; row++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
        }
        panel.Paint += (_, eventArgs) =>
        {
            using Pen border = new(PanelBorderColor, 1F);
            using SolidBrush accent = new(accentColor);
            eventArgs.Graphics.DrawRectangle(
                border,
                0,
                0,
                Math.Max(0, panel.Width - 1),
                Math.Max(0, panel.Height - 1));
            eventArgs.Graphics.FillRectangle(accent, 0, 0, 4, panel.Height);
        };
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Label value)
    {
        Label name = new()
        {
            Text = label.ToUpperInvariant(),
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(name, 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private static void AddActionButton(
        TableLayoutPanel panel,
        ActionButton button,
        int column,
        int row)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(
            column == 0 ? 0 : 6,
            row == 0 ? 0 : 6,
            column == 0 ? 6 : 0,
            row == 0 ? 6 : 0);
        panel.Controls.Add(button, column, row);
    }

    private static Label CreateValueLabel(
        float size = 12F,
        bool autoEllipsis = true)
    {
        return new Label
        {
            Text = "—",
            AutoEllipsis = autoEllipsis,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", size, FontStyle.Bold, GraphicsUnit.Point)
        };
    }

    private static Button CreateCompactButton(string text)
    {
        return new Button
        {
            Text = text,
            BackColor = Color.FromArgb(29, 34, 43),
            ForeColor = SecondaryTextColor,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = PanelBorderColor, BorderSize = 1 },
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand
        };
    }
}
