using System.ComponentModel;
using Ghmr.Controller.Diagnostics;
using Ghmr.Controller;
using Ghmr.Controller.Installation;
using Ghmr.Core.Bridge;
using Ghmr.Core.Launch;

string testDirectory = Path.Combine(Path.GetTempPath(), "ghmr-controller-log-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
try
{
    string concurrentDirectory = Path.Combine(testDirectory, "concurrent");
    using (ControllerDiagnosticLog log = new(concurrentDirectory))
    {
        Parallel.For(0, 200, index => log.Info("Test", $"event={index}"));
    }
    string concurrentPath = Path.Combine(concurrentDirectory, "logs", ControllerDiagnosticLog.FileName);
    string[] entries = File.ReadAllLines(concurrentPath);
    Require(entries.Length == 200, "Concurrent events must survive shutdown flushing.");
    Require(entries.Distinct(StringComparer.Ordinal).Count() == 200,
        "Concurrent events must not overwrite one another.");
    Require(entries.All(line => line.Contains("[INFO] [Test] event=", StringComparison.Ordinal)),
        "Every event must be a complete readable line.");

    using (ControllerDiagnosticLog log = new(concurrentDirectory))
        log.Info("Test", "second-controller-start");
    Require(File.ReadAllLines(concurrentPath).Length == 201,
        "Reopening GHMR must preserve earlier diagnostic evidence.");

    string privateDirectory = Path.Combine(testDirectory, "privacy");
    using (ControllerDiagnosticLog log = new(privateDirectory))
    {
        log.Info("Test", @"C:\Users\private-person\AppData\Local\GHMR" + "\nsecond-line");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        log.Error("Test", "close-failed", new Win32Exception(5, profile + "/file-access-denied"));
    }
    string privatePath = Path.Combine(privateDirectory, "logs", ControllerDiagnosticLog.FileName);
    string privateText = File.ReadAllText(privatePath);
    Require(!privateText.Contains("private-person", StringComparison.Ordinal),
        "Windows user paths must be redacted.");
    Require(privateText.Contains(@"\nsecond-line", StringComparison.Ordinal) &&
            File.ReadAllLines(privatePath).Length == 2,
        "Multiline errors must not inject fake log entries.");
    Require(privateText.Contains("nativeError=5", StringComparison.Ordinal) &&
            privateText.Contains("Win32Exception", StringComparison.Ordinal),
        "Native error codes and exception types must remain available for diagnosis.");

    string rotatingDirectory = Path.Combine(testDirectory, "rotation");
    using (ControllerDiagnosticLog log = new(rotatingDirectory, maxFileBytes: 1024))
    {
        for (int index = 0; index < 100; index++)
            log.Info("Test", $"rotation-event={index}; " + new string('x', 180));
    }
    string[] rotatedFiles = Directory.GetFiles(Path.Combine(rotatingDirectory, "logs"), "*.log");
    Require(rotatedFiles.Length == 4, "Keep the current log and three bounded backups.");
    Require(rotatedFiles.All(path => new FileInfo(path).Length <= 1024),
        "Log rotation must bound disk use.");
    Require(File.ReadAllText(Path.Combine(rotatingDirectory, "logs", ControllerDiagnosticLog.FileName))
            .Contains("rotation-event=99;", StringComparison.Ordinal),
        "The current log must retain the most recent failure evidence.");

    string blockedDirectory = Path.Combine(testDirectory, "blocked");
    Directory.CreateDirectory(blockedDirectory);
    File.WriteAllText(Path.Combine(blockedDirectory, "logs"), "a file blocks the log directory");
    ControllerDiagnosticLog blockedLog = new(blockedDirectory);
    blockedLog.Info("Test", "logging-path-unavailable");
    blockedLog.Dispose();
    Require(blockedLog.WriteFailure is not null,
        "A log-path failure must be observable without throwing into gameplay.");
    blockedLog.Info("Test", "after-dispose");
    blockedLog.Dispose();

    TestBridgeCompatibility(Path.Combine(testDirectory, "bridge-compatibility"));
    await CoordinatorExitSelfTests.RunAsync(Path.Combine(testDirectory, "active-game-exit"));
    RunOptionsSelfTests.Run(Path.Combine(testDirectory, "run-options"));
    await CoordinatorExitSelfTests.TestWindowMediaAsync(Path.Combine(testDirectory, "window-media"));
    await CoordinatorExitSelfTests.TestCompletionRoutesAsync(Path.Combine(testDirectory, "completion-routes"));
    PassScreenSelfTests.Run();
    await CoordinatorExitSelfTests.TestVisualPassAsync(Path.Combine(testDirectory, "visual-pass"));
    CompletedClosePolicySelfTests.Run();
    await CoordinatorExitSelfTests.TestCompletedVManualExitAsync(Path.Combine(testDirectory, "completed-v-manual-exit"));

    if (OperatingSystem.IsWindows())
    {
        await TestGameEventWhileWriterOpenAsync(Path.Combine(testDirectory, "shared-reader"));
        await TestCompletionDuringBlockedAcknowledgementAsync(Path.Combine(testDirectory, "blocked-ack"));
    }
    Console.WriteLine("Controller diagnostic self-test passed: logging, bridge/transport guarding, shared event reads and completion delivery during blocked acknowledgements.");
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void TestBridgeCompatibility(string gameDirectory)
{
    Directory.CreateDirectory(Path.Combine(gameDirectory, "CLEO"));
    string executablePath = Path.Combine(gameDirectory, "SanAndreas.exe");
    File.WriteAllText(executablePath, string.Empty);
    GameLaunchProfile profile = new()
    {
        Game = "sade",
        LaunchExecutablePath = executablePath,
        ProcessName = "SanAndreas"
    };
    string bridgePath = Path.Combine(gameDirectory, "CLEO", "ghmr_sa_bridge[fs].js");

    File.WriteAllText(bridgePath, "const bridgeVersion = \"0.1.1\";");
    BridgeCompatibilityResult stale = BridgeCompatibility.Inspect(profile, "sade");
    Require(!stale.IsCompatible && stale.InstalledVersion == "0.1.1" &&
            stale.RequiredVersion == "0.1.4",
        "The controller must block a stale installed SA bridge before a run.");

    File.WriteAllText(bridgePath, "bridgeVersion: \"0.1.4\"");
    BridgeCompatibilityResult staleTransport = BridgeCompatibility.Inspect(profile, "sade");
    Require(!staleTransport.IsCompatible &&
            staleTransport.Problem.Contains("shared INI transport", StringComparison.Ordinal),
        "A current mission bridge with the old shared transport must be blocked.");

    File.WriteAllText(bridgePath,
        BridgeCompatibility.RequiredIniTransportMarker + "\nbridgeVersion: \"0.1.4\"");
    BridgeCompatibilityResult current = BridgeCompatibility.Inspect(profile, "sade");
    Require(current.IsCompatible && current.InstalledVersion == "0.1.4",
        "The controller must accept the release-matched SA bridge.");

    Require(!BridgeCompatibility.IsReportedVersionCompatible(
            "sade", "0.1.1", out string required) && required == "0.1.4",
        "A stale runtime bridgeReady event must be rejected.");
    Require(BridgeCompatibility.IsReportedVersionCompatible(
            "sade", "0.1.4", out _),
        "The matching runtime bridgeReady event must be accepted.");
}

static async Task TestGameEventWhileWriterOpenAsync(string dataDirectory)
{
    using ControllerDiagnosticLog log = new(dataDirectory);
    await using IniBridgeServer server = new(dataDirectory, log);
    TaskCompletionSource<bool> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int deliveryCount = 0;
    server.MessageReceived = message =>
    {
        Require(message.Type == BridgeEventTypes.PlayerControlGained,
            "An event read while the writer is open must remain intact.");
        Interlocked.Increment(ref deliveryCount);
        delivered.TrySetResult(true);
        return Task.CompletedTask;
    };
    server.Start();
    string controllerPath = Path.Combine(dataDirectory, "ipc", "gta3de-controller.ini");
    string session = File.ReadAllLines(controllerPath)
        .Single(line => line.StartsWith("session=", StringComparison.Ordinal))["session=".Length..];
    string frame = BridgeProtocol.Serialize(new BridgeEnvelope
    {
        Type = BridgeEventTypes.PlayerControlGained,
        MessageId = "shared-read-1",
        Game = "gta3de",
        BridgeSessionId = session,
        RunId = "test-run",
        MissionId = "gta3.espresso_2_go"
    });
    string gamePath = Path.Combine(dataDirectory, "ipc", "gta3de-game.ini");
    // Holding write access exposes a reader that incorrectly uses FileShare.Read.
    using (FileStream gameWriter = new(
        gamePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
    {
        using (StreamWriter writer = new(
            gameWriter, new System.Text.UTF8Encoding(false), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write(
                $"[GHMR]\r\nsession={session}\r\nack=0\r\nsequence=1\r\n" +
                $"part0={frame[..Math.Min(100, frame.Length)]}\r\n" +
                $"part1={(frame.Length > 100 ? frame.Substring(100, Math.Min(100, frame.Length - 100)) : "")}\r\n" +
                $"part2={(frame.Length > 200 ? frame[200..] : "")}\r\n");
        }
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Require(deliveryCount == 1, "The shared read must deliver exactly once.");
    }
}

static async Task TestCompletionDuringBlockedAcknowledgementAsync(string dataDirectory)
{
    string logPath;
    using (ControllerDiagnosticLog log = new(dataDirectory))
    {
        logPath = log.FilePath;
        await using IniBridgeServer server = new(dataDirectory, log);
        TaskCompletionSource<bool> delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> acknowledgementFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int deliveryCount = 0;
        server.MessageReceived = message =>
        {
            Require(message.Type == BridgeEventTypes.MissionCompleted,
                "The accepted completion frame must reach the dispatcher intact.");
            Interlocked.Increment(ref deliveryCount);
            delivered.TrySetResult(true);
            return Task.CompletedTask;
        };
        server.ProtocolError += (_, args) =>
        {
            if (args.Message.Contains("Cannot access the local game bridge", StringComparison.Ordinal))
                acknowledgementFailed.TrySetResult(true);
        };
        server.Start();
        string controllerPath = Path.Combine(dataDirectory, "ipc", "sade-controller.ini");
        string session = File.ReadAllLines(controllerPath)
            .Single(line => line.StartsWith("session=", StringComparison.Ordinal))["session=".Length..];
        string frame = BridgeProtocol.Serialize(new BridgeEnvelope
        {
            Type = BridgeEventTypes.MissionCompleted,
            MessageId = "completion-test-1",
            Game = "sade",
            BridgeSessionId = session,
            RunId = "test-run",
            MissionId = "sa.wrong_side_of_the_tracks"
        });
        using (FileStream blockedAcknowledgement = new(
            controllerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            File.WriteAllText(Path.Combine(dataDirectory, "ipc", "sade-game.ini"),
                $"[GHMR]\r\nsession={session}\r\nack=0\r\nsequence=1\r\npart0={frame}\r\n");
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await acknowledgementFailed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        // Once the external file lock is released, the ordinary heartbeat
        // write must acknowledge the completion without delivering it twice.
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(8));
        bool acknowledged = false;
        while (!acknowledged)
        {
            await Task.Delay(100, deadline.Token);
            try
            {
                acknowledged = File.ReadAllLines(controllerPath)
                    .Any(line => line == "ack=1");
            }
            catch (IOException) { }
        }
        Require(deliveryCount == 1,
            "An acknowledgement retry must not duplicate the mission completion.");
    }
    string diagnosticText = File.ReadAllText(logPath);
    Require(diagnosticText.Contains("Event delivered to dispatcher", StringComparison.Ordinal) &&
            diagnosticText.Contains("Local INI access failed repeatedly", StringComparison.Ordinal),
        "The log must preserve both successful completion delivery and the underlying file-write failure.");
}
