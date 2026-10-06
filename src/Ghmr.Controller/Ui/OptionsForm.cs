using System.ComponentModel;
using System.Diagnostics;
using Ghmr.Controller.Diagnostics;

namespace Ghmr.Controller.Ui;

internal sealed class OptionsForm : Form
{
    private static readonly Color PanelColor = Color.FromArgb(25, 30, 39);
    private static readonly Color BorderColor = Color.FromArgb(54, 63, 77);
    private static readonly Color GoldColor = Color.FromArgb(240, 196, 73);
    private static readonly Color CyanColor = Color.FromArgb(55, 195, 188);
    private static readonly Color SecondaryTextColor = Color.FromArgb(174, 184, 198);

    private readonly ControllerPreferencesStore _preferences;
    private readonly ControllerDiagnosticLog? _log;
    private readonly ComboBox _runOrder = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill
    };
    private readonly ListView _fixedMissions = new()
    {
        View = View.Details, CheckBoxes = true, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill,
        BackColor = PanelColor, ForeColor = Color.White
    };
    private readonly CheckBox _autoResume = new()
    {
        Text = "Automatically select Vice City Resume (experimental)",
        AutoSize = true, ForeColor = Color.White, BackColor = Color.Transparent
    };
    private readonly CheckBox _transitionMusic = new()
    {
        Text = "Play music during cross-game handoffs",
        AutoSize = true,
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point)
    };

    public OptionsForm(ControllerPreferencesStore preferences, ControllerDiagnosticLog? log = null)
    {
        _preferences = preferences;
        _log = log;
        _transitionMusic.Checked = preferences.Current.TransitionMusicEnabled;
        _autoResume.Checked = preferences.Current.ViceCityAutoResumeEnabled;
        _runOrder.Items.AddRange(["Random order (normal beta run)", "Fixed test order (choose below)"]);
        _runOrder.SelectedIndex = preferences.Current.FixedTestOrderEnabled ? 1 : 0;
        _fixedMissions.Columns.Add("Mission — tick to include, select to move", 475);
        string[] selected = preferences.Current.FixedMissionOrder;
        foreach (string id in selected.Concat(BetaRunChoices.MissionIds.Except(selected)))
            _fixedMissions.Items.Add(new ListViewItem(BetaRunChoices.Label(id))
            {
                Tag = id, Checked = selected.Contains(id, StringComparer.Ordinal)
            });

        Text = "GHMR Options";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 740);
        BackColor = Color.FromArgb(10, 13, 19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
    }

    private void BuildLayout()
    {
        GtaBackdropPanel backdrop = new() { Dock = DockStyle.Fill };
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(28, 24, 28, 24),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        TableLayoutPanel heading = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
        heading.Controls.Add(new Label
        {
            Text = "OPTIONS",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = "RUN ORDER  /  TRANSITIONS  /  SUPPORT",
            Dock = DockStyle.Fill,
            ForeColor = CyanColor,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        root.Controls.Add(heading, 0, 0);

        TableLayoutPanel mediaPanel = CreatePanel();
        mediaPanel.RowCount = 3;
        mediaPanel.RowStyles.Clear();
        mediaPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        mediaPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        mediaPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mediaPanel.Controls.Add(_transitionMusic, 0, 0);
        mediaPanel.Controls.Add(_autoResume, 0, 1);
        mediaPanel.Controls.Add(new Label
        {
            Text = "Transition screen and music stop when the next game window appears. " +
                   "Auto Resume recognises the English landing menu; manual Resume remains available.",
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 2);
        root.Controls.Add(mediaPanel, 0, 1);

        TableLayoutPanel orderPanel = new()
        {
            Dock = DockStyle.Fill, BackColor = PanelColor, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(16), Margin = new Padding(0, 8, 0, 8)
        };
        orderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        orderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
        orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        orderPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        orderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        orderPanel.Controls.Add(_runOrder, 0, 0);
        orderPanel.SetColumnSpan(_runOrder, 2);
        orderPanel.Controls.Add(_fixedMissions, 0, 1);
        FlowLayoutPanel moves = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        Button up = CreateButton("UP", CyanColor);
        Button down = CreateButton("DOWN", CyanColor);
        up.Size = down.Size = new Size(58, 34);
        up.Dock = down.Dock = DockStyle.None;
        up.Click += (_, _) => MoveMission(-1);
        down.Click += (_, _) => MoveMission(1);
        moves.Controls.AddRange([up, down]);
        orderPanel.Controls.Add(moves, 1, 1);
        Label orderHelp = new()
        {
            Text = "Fixed routes are diagnostic runs. Random mode always uses all five missions, " +
                   "with a random trilogy game first.", Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor, Font = new Font("Segoe UI", 9F)
        };
        orderPanel.Controls.Add(orderHelp, 0, 2);
        orderPanel.SetColumnSpan(orderHelp, 2);
        void SetOrderEnabled()
        {
            _fixedMissions.Enabled = moves.Enabled = _runOrder.SelectedIndex == 1;
        }
        _runOrder.SelectedIndexChanged += (_, _) => SetOrderEnabled();
        SetOrderEnabled();
        root.Controls.Add(orderPanel, 0, 2);

        TableLayoutPanel supportRow = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 4)
        };
        supportRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        supportRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        Button logsButton = CreateButton("OPEN LOGS", CyanColor);
        logsButton.Margin = new Padding(0, 0, 6, 0);
        logsButton.Click += (_, _) => OpenLogs();
        Button creditsButton = CreateButton("MEDIA CREDITS", GoldColor);
        creditsButton.Margin = new Padding(6, 0, 0, 0);
        creditsButton.Click += (_, _) => ShowCredits();
        supportRow.Controls.Add(logsButton, 0, 0);
        supportRow.Controls.Add(creditsButton, 1, 0);
        root.Controls.Add(supportRow, 0, 3);

        TableLayoutPanel actionRow = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        Label version = new()
        {
            Text = "GHMR v0.4.11 handoff update",
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Button cancel = CreateButton("CANCEL", SecondaryTextColor);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(8, 0, 0, 0);
        Button save = CreateButton("SAVE", GoldColor);
        save.Margin = new Padding(8, 0, 0, 0);
        save.Click += (_, _) => SaveOptions();
        actionRow.Controls.Add(version, 0, 0);
        actionRow.Controls.Add(cancel, 1, 0);
        actionRow.Controls.Add(save, 2, 0);
        root.Controls.Add(actionRow, 0, 4);

        AcceptButton = save;
        CancelButton = cancel;
        backdrop.Controls.Add(root);
        Controls.Add(backdrop);
    }

    private static TableLayoutPanel CreatePanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(22, 20, 22, 16),
            Margin = new Padding(0, 8, 0, 8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Paint += (_, eventArgs) =>
        {
            using Pen border = new(BorderColor, 1F);
            using SolidBrush accent = new(CyanColor);
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

    private static Button CreateButton(string text, Color accent)
    {
        Button button = new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(29, 34, 43),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point)
        };
        button.FlatAppearance.BorderColor = accent;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void SaveOptions()
    {
        try
        {
            string[] selected = _fixedMissions.Items.Cast<ListViewItem>()
                .Where(item => item.Checked).Select(item => (string)item.Tag!).ToArray();
            if (_runOrder.SelectedIndex == 1)
                BetaRunChoices.ValidateFixedOrder(selected);
            if (selected.Length == 0) selected = BetaRunChoices.MissionIds.ToArray();
            _preferences.Save(new ControllerPreferences
            {
                TransitionMusicEnabled = _transitionMusic.Checked,
                FixedTestOrderEnabled = _runOrder.SelectedIndex == 1,
                FixedMissionOrder = selected,
                ViceCityAutoResumeEnabled = _autoResume.Checked
            });
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                this,
                $"GHMR could not save these options.\n\n{exception.Message}",
                "Options not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void MoveMission(int offset)
    {
        if (_fixedMissions.SelectedIndices.Count != 1) return;
        int index = _fixedMissions.SelectedIndices[0];
        int destination = index + offset;
        if (destination < 0 || destination >= _fixedMissions.Items.Count) return;
        ListViewItem item = _fixedMissions.Items[index];
        _fixedMissions.Items.RemoveAt(index);
        _fixedMissions.Items.Insert(destination, item);
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    private void OpenLogs()
    {
        try
        {
            if (_log?.WriteFailure is { } failure)
            {
                MessageBox.Show(this,
                    $"GHMR could not write its controller log this session.\n\n{failure}",
                    "Controller logging unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string logsDirectory = Path.Combine(_preferences.DataDirectory, "logs");
            Directory.CreateDirectory(logsDirectory);
            _log?.Info("UI", "Opening controller logs folder.");
            Process.Start(new ProcessStartInfo
            {
                FileName = logsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            Win32Exception)
        {
            _log?.Error("UI", "Could not open controller logs folder", exception);
            MessageBox.Show(
                this,
                $"GHMR could not open the logs folder.\n\n{exception.Message}",
                "Logs unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ShowCredits()
    {
        MessageBox.Show(
            this,
            "Transition thumbnails: creator-supplied channel artwork.\n\n" +
            "Transition music: creator-supplied no-attribution track. " +
            "Permission details are recorded in the release media notes.",
            "GHMR Media Credits",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
