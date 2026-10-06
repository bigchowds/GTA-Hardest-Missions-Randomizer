using Ghmr.Controller.Installation;
using Ghmr.Controller.Launch;
using Ghmr.Controller.Diagnostics;
using Ghmr.Core.Launch;

namespace Ghmr.Controller.Ui;

public sealed class GameSetupForm : Form
{
    private static readonly Color PanelColor = Color.FromArgb(25, 30, 39);
    private static readonly Color BorderColor = Color.FromArgb(54, 63, 77);
    private static readonly Color CyanColor = Color.FromArgb(55, 195, 188);
    private static readonly Color SecondaryTextColor = Color.FromArgb(174, 184, 198);

    private static readonly (string Id, string Name)[] Games =
    [
        ("gta3de", "GTA III: Definitive Edition"),
        ("vcde", "GTA Vice City: Definitive Edition"),
        ("sade", "GTA San Andreas: Definitive Edition"),
        ("gta4", "GTA IV: Complete Edition"),
        ("gtav_enhanced", "GTA V Enhanced")
    ];

    private readonly GameLaunchProfileStore _profiles;
    private readonly ControllerDiagnosticLog? _log;
    private readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _executable = new();
    private readonly TextBox _processName = new();

    public GameSetupForm(
        GameLaunchProfileStore profiles,
        ControllerDiagnosticLog? log = null,
        string? initialGame = null)
    {
        _profiles = profiles;
        _log = log;
        Text = "Setup Game Bridges";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 365);
        BackColor = Color.FromArgb(10, 13, 19);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        _game.BackColor = PanelColor;
        _game.ForeColor = Color.White;
        _game.FlatStyle = FlatStyle.Flat;
        StyleTextBox(_executable);
        StyleTextBox(_processName);

        foreach ((string _, string name) in Games)
        {
            _game.Items.Add(name);
        }

        _game.SelectedIndexChanged += (_, _) => LoadSelectedGame();
        BuildLayout();
        int initialIndex = Array.FindIndex(
            Games,
            game => string.Equals(game.Id, initialGame, StringComparison.Ordinal));
        _game.SelectedIndex = initialIndex >= 0 ? initialIndex : 0;
    }

    private string SelectedGameId => Games[_game.SelectedIndex].Id;

    private void BuildLayout()
    {
        GtaBackdropPanel backdrop = new() { Dock = DockStyle.Fill };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 16, 24, 20),
            ColumnCount = 3,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

        TableLayoutPanel heading = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
        heading.Controls.Add(new Label
        {
            Text = "SETUP GAME BRIDGES",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.BottomLeft
        });
        heading.Controls.Add(new Label
        {
            Text = "LOCAL PATHS  /  READABLE ADAPTERS  /  SAFE REPAIR",
            Dock = DockStyle.Fill,
            ForeColor = CyanColor,
            Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 3);

        AddLabel(layout, "Game", 1);
        _game.Dock = DockStyle.Fill;
        _game.Margin = new Padding(3, 5, 3, 5);
        layout.Controls.Add(_game, 1, 1);
        layout.SetColumnSpan(_game, 2);

        AddLabel(layout, "Launch executable", 2);
        _executable.Dock = DockStyle.Fill;
        _executable.Margin = new Padding(3, 6, 6, 6);
        layout.Controls.Add(_executable, 1, 2);
        Button browse = CreateButton("Browse...", PanelColor, Color.White);
        browse.Dock = DockStyle.Fill;
        browse.Margin = new Padding(0, 4, 0, 4);
        browse.Click += (_, _) => BrowseExecutable();
        layout.Controls.Add(browse, 2, 2);

        AddLabel(layout, "Game process", 3);
        _processName.Dock = DockStyle.Fill;
        _processName.Margin = new Padding(3, 6, 6, 6);
        layout.Controls.Add(_processName, 1, 3);
        Label suffix = new()
        {
            Text = "without .exe",
            Dock = DockStyle.Fill,
            ForeColor = SecondaryTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        layout.Controls.Add(suffix, 2, 3);

        Label explanation = new()
        {
            Text = "Select the actual gameplay EXE, not a launcher. Paths stay on this PC. GHMR normally closes games safely; after a confirmed GTA V pass it directly closes that verified game process.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = Color.FromArgb(210, PanelColor),
            ForeColor = SecondaryTextColor,
            Padding = new Padding(14),
            Margin = new Padding(0, 8, 0, 8)
        };
        layout.Controls.Add(explanation, 0, 4);
        layout.SetColumnSpan(explanation, 3);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 0, 0)
        };
        Button close = CreateButton("Close", PanelColor, Color.White);
        close.Width = 94;
        close.DialogResult = DialogResult.Cancel;
        Button save = CreateButton("Save", PanelColor, Color.White);
        save.Width = 94;
        Button installBridge = CreateButton(
            "Install / Repair Bridge",
            CyanColor,
            Color.FromArgb(8, 18, 20));
        installBridge.Width = 178;
        save.Click += (_, _) => SaveSelectedGame();
        installBridge.Click += (_, _) => InstallSelectedBridge();
        actions.Controls.Add(close);
        actions.Controls.Add(save);
        actions.Controls.Add(installBridge);
        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 3);

        AcceptButton = save;
        CancelButton = close;
        backdrop.Controls.Add(layout);
        Controls.Add(backdrop);
    }

    private void LoadSelectedGame()
    {
        GameLaunchProfile? profile = _profiles.Find(SelectedGameId);
        _executable.Text = profile?.LaunchExecutablePath ?? string.Empty;
        _processName.Text = profile?.ProcessName ?? string.Empty;
    }

    private void BrowseExecutable()
    {
        using OpenFileDialog picker = new()
        {
            Title = $"Select the launch executable for {Games[_game.SelectedIndex].Name}",
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _executable.Text = picker.FileName;
        _processName.Text = Path.GetFileNameWithoutExtension(picker.FileName);
    }

    private void SaveSelectedGame()
    {
        try
        {
            _profiles.SetFromExecutable(
                SelectedGameId,
                _executable.Text,
                _processName.Text);
            MessageBox.Show(
                this,
                $"Saved {Games[_game.SelectedIndex].Name}.",
                "Game path saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Game path was not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void InstallSelectedBridge()
    {
        if (SelectedGameId is not (
                "sade" or "gta3de" or "vcde" or "gta4" or "gtav_enhanced"))
        {
            MessageBox.Show(
                this,
                "This bridge is not implemented yet.",
                "Bridge not ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_executable.Text))
        {
            BrowseExecutable();
            if (string.IsNullOrWhiteSpace(_executable.Text))
            {
                return;
            }
        }

        try
        {
            _log?.Info("Setup", $"Bridge repair requested; game={SelectedGameId}; " +
                $"executable={Path.GetFileName(_executable.Text)}");
            string processName;
            if (SelectedGameId == "sade")
            {
                SanAndreasBridgeInstaller.Install(_executable.Text);
                processName = "SanAndreas";
            }
            else if (SelectedGameId == "gta3de")
            {
                Gta3BridgeInstaller.Install(_executable.Text);
                processName = "LibertyCity";
            }
            else if (SelectedGameId == "vcde")
            {
                ViceCityBridgeInstaller.Install(_executable.Text);
                processName = "ViceCity";
            }
            else if (SelectedGameId == "gta4")
            {
                Gta4BridgeInstaller.Install(_executable.Text);
                processName = "GTAIV";
            }
            else
            {
                Gta5BridgeInstaller.Install(_executable.Text);
                processName = "GTA5_Enhanced";
            }

            _processName.Text = processName;
            _profiles.SetFromExecutable(
                SelectedGameId,
                _executable.Text,
                processName);
            GameLaunchProfile profile = _profiles.Find(SelectedGameId)
                ?? throw new InvalidOperationException(
                    "The saved game profile could not be read back.");
            BridgeCompatibility.VerifyInstalled(profile);
            BridgeCompatibilityResult verified = BridgeCompatibility.Inspect(
                profile,
                SelectedGameId);
            _log?.Info("Setup", $"Bridge repair verified; game={SelectedGameId}; " +
                $"version={verified.InstalledVersion}; script={verified.ScriptPath}");
            MessageBox.Show(
                this,
                SelectedGameId == "gtav_enhanced"
                    ? $"The GTA V bridge v{verified.InstalledVersion} is installed and verified. " +
                      "Its launch path is saved. " +
                      "Once Story Mode reaches stable free roam, GHMR requests Derailed " +
                      "and confirms Rockstar's restore-point prompt automatically."
                    : $"Bridge v{verified.InstalledVersion} for " +
                      $"{Games[_game.SelectedIndex].Name} is installed and verified. " +
                      "Its launch path is saved.",
                "Bridge ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (UnauthorizedAccessException exception)
        {
            _log?.Error("Setup", $"Windows denied bridge repair; game={SelectedGameId}", exception);
            MessageBox.Show(
                this,
                "Windows blocked changes to the game folder. Close GHMR, run it as administrator for this installation step only, install the bridge, then reopen GHMR normally.",
                "One-time permission required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or ArgumentException or
            InvalidOperationException)
        {
            _log?.Error("Setup", $"Bridge repair failed; game={SelectedGameId}", exception);
            MessageBox.Show(
                this,
                exception.Message,
                "Bridge was not installed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void AddLabel(TableLayoutPanel layout, string text, int row)
    {
        layout.Controls.Add(
            new Label
            {
                Text = text.ToUpperInvariant(),
                Dock = DockStyle.Fill,
                ForeColor = SecondaryTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point)
            },
            0,
            row);
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = PanelColor;
        textBox.ForeColor = Color.White;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static Button CreateButton(
        string text,
        Color background,
        Color foreground)
    {
        return new Button
        {
            Text = text,
            Height = 36,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = BorderColor, BorderSize = 1 },
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand
        };
    }
}
