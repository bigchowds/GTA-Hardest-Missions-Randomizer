using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Ghmr.Core.Bridge;
using Ghmr.Controller.Diagnostics;
using System.Diagnostics;

namespace Ghmr.Controller;

// Each side owns one INI file. Sequence plus a length/checksum pair prevents a
// reader from accepting a mixture of old and new chunks during an in-place
// update. Acknowledgements let the other side reuse the single slot safely.
public sealed class IniBridgeServer : IAsyncDisposable
{
    private const int IoFailureWarningThreshold = 3;
    // Every CLEO/INI adapter must have a controller slot here. Keeping this
    // list explicit prevents the controller from writing files for games that
    // do not yet have a production bridge.
    private static readonly string[] Games =
        ["sade", "gta3de", "vcde", "gta4", "gtav_enhanced"];
    private readonly string _directory;
    private readonly ControllerDiagnosticLog? _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private readonly Channel<BridgeEnvelope> _messages = Channel.CreateUnbounded<BridgeEnvelope>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private Task? _pollTask;
    private Task? _dispatchTask;

    public IniBridgeServer(string dataDirectory, ControllerDiagnosticLog? log = null)
    {
        _log = log;
        _directory = Path.Combine(dataDirectory, "ipc");
        Directory.CreateDirectory(_directory);
        foreach (string game in Games)
        {
            _slots.Add(game, new Slot(
                Path.Combine(_directory, game + "-controller.ini"),
                Path.Combine(_directory, game + "-game.ini")));
        }
    }

    public Func<BridgeEnvelope, Task>? MessageReceived { get; set; }
    public event EventHandler<BridgeConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<BridgeProtocolErrorEventArgs>? ProtocolError;

    public void Start()
    {
        if (_pollTask is not null) return;
        _log?.Info("Transport", "Starting INI polling and bridge event dispatcher.");
        Reset();
        _dispatchTask = Task.Run(() => DispatchAsync(_shutdown.Token));
        _pollTask = Task.Run(() => PollAsync(_shutdown.Token));
    }

    public void Reset()
    {
        foreach (Slot slot in _slots.Values)
        {
            ResetSlot(slot);
        }
    }

    // Give the game being launched at a cross-title handoff a new transport
    // generation. A bridge that was left alive, reloaded by the launcher, or
    // still has an acknowledged command from an earlier test must introduce
    // itself again before it can control the new mission.
    public void PrepareForLaunch(string game)
    {
        if (!_slots.TryGetValue(game, out Slot? slot))
            throw new InvalidOperationException(
                "No supported game bridge is configured.");

        bool wasConnected = ResetSlot(slot);
        if (wasConnected)
        {
            ConnectionChanged?.Invoke(
                this,
                new BridgeConnectionEventArgs(
                    connected: false,
                    game: slot.Game));
        }
    }

    public Task SendAsync(BridgeEnvelope command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_slots.TryGetValue(command.Game, out Slot? slot))
            throw new InvalidOperationException("No supported game bridge is configured.");
        slot.Commands.Enqueue(BridgeProtocol.Serialize(command));
        _log?.Info("Transport", $"Command queued; type={command.Type}; game={command.Game}; " +
            $"run={command.RunId}; mission={command.MissionId}; session={command.BridgeSessionId}");
        return Task.CompletedTask;
    }

    public async Task<bool> SendAndWaitForAckAsync(
        BridgeEnvelope command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_slots.TryGetValue(command.Game, out Slot? slot))
            throw new InvalidOperationException("No supported game bridge is configured.");
        string encoded = BridgeProtocol.Serialize(command);
        slot.Commands.Enqueue(encoded);
        Stopwatch acknowledgementTimer = Stopwatch.StartNew();
        _log?.Info("Transport", $"Command queued with acknowledgement wait; type={command.Type}; " +
            $"game={command.Game}; timeoutMs={timeout.TotalMilliseconds}");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _shutdown.Token);
        deadline.CancelAfter(timeout);
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(deadline.Token).ConfigureAwait(false))
            {
                if (slot.OutgoingSequence > 0 &&
                    slot.OutgoingSequence == slot.AcknowledgedSequence &&
                    slot.Outgoing == encoded)
                {
                    _log?.Info("Transport", $"Command acknowledged; type={command.Type}; " +
                        $"game={command.Game}; elapsedMs={acknowledgementTimer.ElapsedMilliseconds}");
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        _log?.Warning("Transport", $"Command acknowledgement timed out; type={command.Type}; " +
            $"game={command.Game}; elapsedMs={acknowledgementTimer.ElapsedMilliseconds}");
        return false;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (Slot slot in _slots.Values)
            {
                try
                {
                    lock (slot.IoGate)
                    {
                        ReadGame(slot);
                        if (slot.OutgoingSequence == slot.AcknowledgedSequence &&
                            slot.Commands.TryDequeue(out string? command) &&
                            command is not null)
                        {
                            slot.OutgoingSequence++;
                            slot.Outgoing = command;
                            WriteController(slot);
                        }
                        else if (DateTime.UtcNow - slot.LastPulse > TimeSpan.FromSeconds(1))
                        {
                            WriteController(slot);
                        }
                    }

                    if (slot.ConsecutiveIoFailures > 0)
                    {
                        _log?.Info("Transport", $"Local INI access recovered; game={slot.Game}; " +
                            $"failedPolls={slot.ConsecutiveIoFailures}");
                        slot.ConsecutiveIoFailures = 0;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    slot.ConsecutiveIoFailures++;
                    if (slot.ConsecutiveIoFailures == 1)
                    {
                        _log?.Warning("Transport", $"Transient local INI access failed; " +
                            $"game={slot.Game}; retrying on the next poll; " +
                            $"exception={exception.GetType().Name}; message={exception.Message}");
                    }
                    else if (slot.ConsecutiveIoFailures == IoFailureWarningThreshold)
                    {
                        _log?.Error("Transport", $"Local INI access failed repeatedly; " +
                            $"game={slot.Game}; failedPolls={slot.ConsecutiveIoFailures}", exception);
                        ProtocolError?.Invoke(this, new BridgeProtocolErrorEventArgs(
                            "Cannot access the local game bridge after repeated retries: " +
                            exception.Message));
                    }
                }
            }
        }
    }

    private void ReadGame(Slot slot)
    {
        if (!File.Exists(slot.GamePath)) return;
        Dictionary<string, string> values = ReadIni(slot.GamePath);
        if (Get(values, "session") != slot.Session) return;

        if (int.TryParse(Get(values, "ack"), out int acknowledgement) &&
            acknowledgement == slot.OutgoingSequence &&
            acknowledgement > slot.AcknowledgedSequence)
        {
            slot.AcknowledgedSequence = acknowledgement;
        }

        if (!int.TryParse(Get(values, "sequence"), out int sequence) ||
            sequence <= slot.IncomingSequence) return;
        string raw = Get(values, "part0") + Get(values, "part1") + Get(values, "part2");
        // ScriptHook bridges rewrite their tiny INI file in place. A polling
        // read can therefore briefly see the new sequence before all JSON
        // chunks are visible. That is ordinary transport contention, not a
        // player-facing bridge failure: leave the sequence unacknowledged and
        // retry it on the next poll.
        if (string.IsNullOrWhiteSpace(raw) ||
            raw[0] != '{' || raw[^1] != '}') return;

        BridgeEnvelope message;
        try
        {
            message = BridgeProtocol.Deserialize(raw);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or System.Text.Json.JsonException)
        {
            // A complete-looking mixture of old and new chunks is still a
            // possible in-place-write race. Do not acknowledge it; the writer
            // will expose the complete frame on a later poll.
            return;
        }

        if (message.Game != slot.Game)
        {
            ProtocolError?.Invoke(this, new BridgeProtocolErrorEventArgs(
                "Game bridge sent the wrong game id."));
            return;
        }

        // Deliver before advancing the acknowledgement. If writing the INI
        // acknowledgement fails, the already-delivered event must still reach
        // the run engine and must not be silently skipped on the next poll.
        if (!_messages.Writer.TryWrite(message))
        {
            _log?.Warning("Transport", $"Event queue unavailable; game={slot.Game}; " +
                $"event={message.Type}; sequence={sequence}; acknowledgement withheld");
            return;
        }
        slot.IncomingSequence = sequence;
        if (message.Type != BridgeEventTypes.Heartbeat)
            _log?.Info("Transport", $"Event delivered to dispatcher; game={slot.Game}; " +
                $"event={message.Type}; sequence={sequence}; message={message.MessageId}");
        WriteController(slot);
        if (!slot.Connected)
        {
            slot.Connected = true;
            ConnectionChanged?.Invoke(
                this,
                new BridgeConnectionEventArgs(
                    connected: true,
                    game: slot.Game));
        }
    }

    private bool ResetSlot(Slot slot)
    {
        lock (slot.IoGate)
        {
            bool wasConnected = slot.Connected;
            while (slot.Commands.TryDequeue(out _)) { }
            slot.OutgoingSequence = 0;
            slot.IncomingSequence = 0;
            slot.AcknowledgedSequence = 0;
            slot.Connected = false;
            slot.Outgoing = "";
            slot.ConsecutiveIoFailures = 0;
            // The token changes every run and immediately before every cross-game
            // launch. Stale game events and recovered commands are then ignored.
            slot.Session = Guid.NewGuid().ToString("N");
            WriteController(slot);
            _log?.Info("Transport", $"Slot reset; game={slot.Game}; session={slot.Session}");
            return wasConnected;
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (BridgeEnvelope message in
                _messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (MessageReceived is not { } handler) continue;
                try { await handler(message).ConfigureAwait(false); }
                catch (Exception exception)
                {
                    _log?.Error("Bridge", $"Event handler failed; game={message.Game}; " +
                        $"event={message.Type}; message={message.MessageId}", exception);
                    ProtocolError?.Invoke(this, new BridgeProtocolErrorEventArgs(
                        "Bridge event could not be processed: " + exception.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void WriteController(Slot slot)
    {
        // CLEO's IniFiles plugin briefly opens this file for each key it reads.
        // Replacing the pathname with File.Move therefore races the plugin on
        // Windows: an open reader commonly shares read/write access, but not
        // delete access, so the rename fails with ERROR_ACCESS_DENIED. Write the
        // small frame in place with read/write sharing instead. The JS side
        // already validates sequence before and after reading all chunks and
        // will simply retry a frame observed during this sub-millisecond write.
        string[] chunks = Chunks(slot.Outgoing);
        string checksum = FrameChecksum(slot.Outgoing);
        string content = "[GHMR]\r\nsession=" + slot.Session +
            "\r\npulse=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            "\r\nsequence=" + slot.OutgoingSequence +
            "\r\nack=" + slot.IncomingSequence +
            "\r\nlength=" + slot.Outgoing.Length +
            "\r\nchecksum=" + checksum +
            "\r\npart0=" + chunks[0] +
            "\r\npart1=" + chunks[1] +
            "\r\npart2=" + chunks[2] + "\r\n";
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);

        lock (slot.IoGate)
        {
            Exception? lastError = null;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using FileStream stream = new(
                        slot.ControllerPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: false);
                    slot.LastPulse = DateTime.UtcNow;
                    return;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    lastError = exception;
                    Thread.Sleep(10);
                }
            }

            throw new IOException(
                $"Could not update '{slot.ControllerPath}' after retrying the active INI reader.",
                lastError);
        }
    }

    private static string[] Chunks(string value)
    {
        if (value.Length > BridgeProtocol.MaxMessageUtf8Bytes || value.Any(c => c > 127))
            throw new InvalidDataException("Only bounded ASCII bridge JSON is supported.");
        string[] parts = ["", "", ""];
        for (int i = 0; i < value.Length; i += 100)
            parts[i / 100] = value.Substring(i, Math.Min(100, value.Length - i));
        return parts;
    }

    private static string FrameChecksum(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            if (character > 127)
                throw new InvalidDataException("Only ASCII bridge JSON is supported.");
            hash ^= character;
            hash *= 16777619;
        }
        return hash.ToString("x8");
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        // Reading an event must not deny the game's simultaneous INI writes.
        using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        while (reader.ReadLine() is string line)
        {
            int equal = line.IndexOf('=');
            if (equal > 0) result[line[..equal]] = line[(equal + 1)..].Trim();
        }
        return result;
    }

    private static string Get(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : "";

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _messages.Writer.TryComplete();
        if (_dispatchTask is not null) await _dispatchTask.ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private sealed class Slot(string controllerPath, string gamePath)
    {
        public object IoGate { get; } = new();
        public string Game { get; } = Path.GetFileName(gamePath).Split('-')[0];
        public string ControllerPath { get; } = controllerPath;
        public string GamePath { get; } = gamePath;
        public string Session { get; set; } = "";
        public ConcurrentQueue<string> Commands { get; } = new();
        public int OutgoingSequence { get; set; }
        public int IncomingSequence { get; set; }
        public int AcknowledgedSequence { get; set; }
        public string Outgoing { get; set; } = "";
        public bool Connected { get; set; }
        public DateTime LastPulse { get; set; }
        public int ConsecutiveIoFailures { get; set; }
    }
}
