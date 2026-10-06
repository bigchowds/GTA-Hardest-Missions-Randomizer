using Ghmr.Controller.Launch;
using Ghmr.Controller.Ui;
using Ghmr.Controller.Diagnostics;
using Ghmr.Core;
using Ghmr.Core.Launch;
using Ghmr.Core.Run;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Ghmr.Controller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR", "controller");
        using ControllerDiagnosticLog log = new(dataDirectory);
        string version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        log.Info("Controller", $"Starting GHMR version={version}; pid={Environment.ProcessId}; " +
            $"runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; " +
            $"process64bit={Environment.Is64BitProcess}");
        UnhandledExceptionEventHandler fatalHandler = (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                log.Error("Controller", "Unhandled fatal exception", exception);
            log.Dispose();
        };
        EventHandler<UnobservedTaskExceptionEventArgs> taskHandler = (_, args) =>
            log.Error("Controller", "Unobserved background task exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += fatalHandler;
        TaskScheduler.UnobservedTaskException += taskHandler;

        try
        {
            string catalogPath = Path.Combine(
                AppContext.BaseDirectory,
                "config",
                "missions.v0.1.json");
            MissionCatalog catalog = MissionCatalog.Load(catalogPath);
            FileRunPersistence persistence = new(dataDirectory);
            FileAuditSink audit = new(dataDirectory);
            RunSnapshot? previous = persistence.Load();
            if (previous is not null && !previous.IsTerminal && previous.Phase != RunPhase.Idle)
            {
                log.Warning("Run", $"Previous controller session ended mid-run; " +
                    $"run={previous.RunId}; phase={previous.Phase}; game={previous.ActiveGame}");
                RunEngine.MarkInterrupted(previous, persistence, audit);
            }

            RunEngine engine = new(catalog, persistence, audit);
            IniBridgeServer bridgeServer = new(dataDirectory, log);
            GameLaunchProfileStore profiles = new(dataDirectory);
            ControllerPreferencesStore preferences = new(dataDirectory);
            WindowsGameProcessManager processes = new(log);
            GameHandoffService handoff = new(profiles, processes);
            ControllerCoordinator coordinator = new(
                engine, bridgeServer, handoff, log, profiles, processes);
            log.Info("Controller", $"Transition music enabled={preferences.Current.TransitionMusicEnabled}");
            Application.Run(new MainForm(coordinator, profiles, preferences, log));
        }
        catch (Exception exception)
        {
            log.Error("Controller", "Startup or main application loop failed", exception);
            MessageBox.Show(
                $"GHMR could not start.\n\n{exception.Message}",
                "GHMR startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            log.Info("Controller", "Controller session ended.");
            AppDomain.CurrentDomain.UnhandledException -= fatalHandler;
            TaskScheduler.UnobservedTaskException -= taskHandler;
        }
    }
}
