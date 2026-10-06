using System.IO.Pipes;
using System.Text;
using Ghmr.Core.Bridge;

namespace Ghmr.SimulatedBridge;

internal static class Program
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private static readonly string BridgeSessionId = Guid.NewGuid().ToString("N");
    private static long _messageSequence;
    private static string _game = "vcde";
    private static string? _runId;
    private static string? _missionId;

    private static async Task<int> Main(string[] args)
    {
        _game = ReadArgument(args, "--game=") ?? "vcde";
        using CancellationTokenSource shutdown = new();
        await using NamedPipeClientStream pipe = new(
            ".",
            BridgeProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Console.WriteLine($"Connecting simulated {_game} bridge...");
        try
        {
            await pipe.ConnectAsync(5000, shutdown.Token);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Controller pipe was not available. Start GHMR.Controller first.");
            return 1;
        }

        using StreamReader reader = new(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        using StreamWriter writer = new(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        await SendAsync(
            writer,
            NewEvent(BridgeEventTypes.BridgeReady) with
            {
                BridgeVersion = "0.1.0-simulated",
                ExecutableVersion = "development"
            },
            shutdown.Token);

        Task readTask = ReadCommandsAsync(reader, writer, shutdown.Token);
        Task inputTask = Task.Run(() => ReadInputAsync(writer, shutdown), shutdown.Token);
        await Task.WhenAny(readTask, inputTask);
        shutdown.Cancel();

        try
        {
            await readTask;
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    private static async Task ReadCommandsAsync(
        StreamReader reader,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return;
            }

            BridgeEnvelope command = BridgeProtocol.Deserialize(line);
            _runId = command.RunId;
            _missionId = command.MissionId;
            Console.WriteLine($"Controller: {command.Type} ({command.MissionId})");

            switch (command.Type)
            {
                case ControllerCommandTypes.PrepareMission:
                    await SendAsync(
                        writer,
                        NewEvent(BridgeEventTypes.MissionPrepared),
                        cancellationToken);
                    break;

                case ControllerCommandTypes.StartMission:
                case ControllerCommandTypes.RestartMission:
                    await SendAsync(
                        writer,
                        NewEvent(BridgeEventTypes.PlayerControlGained),
                        cancellationToken);
                    PrintControls();
                    break;

                case ControllerCommandTypes.AbortRun:
                case ControllerCommandTypes.ReleaseBridge:
                    return;
            }
        }
    }

    private static async Task ReadInputAsync(
        StreamWriter writer,
        CancellationTokenSource shutdown)
    {
        PrintControls();
        while (!shutdown.IsCancellationRequested)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            string? eventType = key.Key switch
            {
                ConsoleKey.F => BridgeEventTypes.MissionFailed,
                ConsoleKey.C => BridgeEventTypes.MissionCompleted,
                ConsoleKey.L => BridgeEventTypes.PlayerControlLost,
                ConsoleKey.G => BridgeEventTypes.PlayerControlGained,
                _ => null
            };

            if (key.Key == ConsoleKey.Q)
            {
                shutdown.Cancel();
                return;
            }

            if (eventType is null)
            {
                continue;
            }

            if (_missionId is null || _runId is null)
            {
                Console.WriteLine("Wait until the controller sends a mission command.");
                continue;
            }

            await SendAsync(writer, NewEvent(eventType), shutdown.Token);
            Console.WriteLine($"Bridge: {eventType}");
        }
    }

    private static BridgeEnvelope NewEvent(string type)
    {
        return new BridgeEnvelope
        {
            Type = type,
            MessageId = $"sim-{Interlocked.Increment(ref _messageSequence)}",
            Game = _game,
            BridgeSessionId = BridgeSessionId,
            RunId = _runId,
            MissionId = _missionId
        };
    }

    private static async Task SendAsync(
        StreamWriter writer,
        BridgeEnvelope message,
        CancellationToken cancellationToken)
    {
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(BridgeProtocol.Serialize(message));
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    private static string? ReadArgument(IEnumerable<string> args, string prefix)
    {
        string? argument = args.FirstOrDefault(
            value => value.StartsWith(prefix, StringComparison.Ordinal));
        return argument is null ? null : argument[prefix.Length..];
    }

    private static void PrintControls()
    {
        Console.WriteLine("F = fail, C = complete, L = control lost, G = control gained, Q = quit");
    }
}
