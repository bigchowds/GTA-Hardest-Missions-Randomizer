using System.IO.Pipes;
using System.Text;
using Ghmr.Core.Bridge;

namespace Ghmr.Controller;

public sealed class BridgeConnectionEventArgs(
    bool connected,
    string? game = null) : EventArgs
{
    public bool Connected { get; } = connected;
    public string? Game { get; } = game;
}

public sealed class BridgeProtocolErrorEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class BridgeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _writerSync = new();

    private Task? _listenTask;
    private StreamWriter? _writer;

    public Func<BridgeEnvelope, Task>? MessageReceived { get; set; }

    public event EventHandler<BridgeConnectionEventArgs>? ConnectionChanged;
    public event EventHandler<BridgeProtocolErrorEventArgs>? ProtocolError;

    public void Start()
    {
        if (_listenTask is not null)
        {
            return;
        }

        _listenTask = Task.Run(() => ListenLoopAsync(_shutdown.Token));
    }

    public async Task SendAsync(BridgeEnvelope command, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StreamWriter writer;
            lock (_writerSync)
            {
                writer = _writer
                    ?? throw new InvalidOperationException("No game bridge is connected.");
            }

            await writer.WriteLineAsync(BridgeProtocol.Serialize(command)).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _writeGate.Dispose();
        _shutdown.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe = new(
                BridgeProtocol.PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
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

            lock (_writerSync)
            {
                _writer = writer;
            }
            ConnectionChanged?.Invoke(this, new BridgeConnectionEventArgs(connected: true));

            try
            {
                while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    BridgeEnvelope message;
                    try
                    {
                        message = BridgeProtocol.Deserialize(line);
                    }
                    catch (Exception exception) when (
                        exception is InvalidDataException or System.Text.Json.JsonException)
                    {
                        ProtocolError?.Invoke(
                            this,
                            new BridgeProtocolErrorEventArgs(exception.Message));
                        continue;
                    }

                    Func<BridgeEnvelope, Task>? handler = MessageReceived;
                    if (handler is not null)
                    {
                        try
                        {
                            await handler(message).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            ProtocolError?.Invoke(
                                this,
                                new BridgeProtocolErrorEventArgs(
                                    $"Bridge event could not be processed: {exception.Message}"));
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_writerSync)
                {
                    if (ReferenceEquals(_writer, writer))
                    {
                        _writer = null;
                    }
                }
                ConnectionChanged?.Invoke(this, new BridgeConnectionEventArgs(connected: false));
            }
        }
    }
}
