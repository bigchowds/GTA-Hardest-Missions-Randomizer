using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Ghmr.Controller.Diagnostics;

// Producers only enqueue bounded text. Disk writes, flushing and rotation run
// on a separate task, never on the UI or serialized bridge dispatcher.
public sealed class ControllerDiagnosticLog : IDisposable
{
    public const string FileName = "GHMR-controller.log";
    private const int BackupCount = 3;
    private const int MaxMessageLength = 8192;
    private readonly Channel<string> _entries = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly string _userProfile;
    private readonly long _maxFileBytes;
    private readonly Task _writerTask;
    private string? _writeFailure;
    private int _completed;
    private int _droppedEntries;

    public ControllerDiagnosticLog(string dataDirectory, long maxFileBytes = 2 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (maxFileBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        DirectoryPath = Path.Combine(dataDirectory, "logs");
        FilePath = Path.Combine(DirectoryPath, FileName);
        _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _maxFileBytes = maxFileBytes;
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public string DirectoryPath { get; }
    public string FilePath { get; }
    public string? WriteFailure => Volatile.Read(ref _writeFailure);

    public void Info(string area, string message) => Write("INFO", area, message);
    public void Warning(string area, string message) => Write("WARN", area, message);

    public void Error(string area, string message, Exception exception)
    {
        string nativeCode = exception is Win32Exception win32
            ? $"; nativeError={win32.NativeErrorCode}"
            : string.Empty;
        Write("ERROR", area,
            $"{message}; exception={exception.GetType().Name}; " +
            $"hresult=0x{exception.HResult:x8}{nativeCode}; {exception}");
    }

    private void Write(string level, string area, string message)
    {
        if (Volatile.Read(ref _completed) != 0 || WriteFailure is not null)
            return;
        string timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        string entry = $"{timestamp} [{level}] [{Clean(area)}] {Clean(message)}";
        if (!_entries.Writer.TryWrite(entry))
            Interlocked.Increment(ref _droppedEntries);
    }

    private string Clean(string text)
    {
        if (!string.IsNullOrEmpty(_userProfile))
            text = text.Replace(_userProfile, "<userprofile>", StringComparison.OrdinalIgnoreCase);
        // Exception messages can contain a different Windows user path.
        text = Regex.Replace(text, @"(?i)([a-z]:\\Users\\)[^\\\r\n]+", "$1<user>");
        text = text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return text.Length <= MaxMessageLength
            ? text
            : text[..MaxMessageLength] + " [truncated]";
    }

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            writer = OpenWriter();
            await foreach (string entry in _entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                int dropped = Interlocked.Exchange(ref _droppedEntries, 0);
                if (dropped > 0)
                    WriteLine(ref writer,
                        $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [WARN] [Logging] " +
                        $"Dropped {dropped} entries because the diagnostic queue was full.");
                WriteLine(ref writer, entry);
            }
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            // Logging failure must not invalidate a run or block game switching.
            Volatile.Write(ref _writeFailure,
                Clean($"{exception.GetType().Name}: {exception.Message}"));
        }
        finally
        {
            try { writer?.Dispose(); }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                Volatile.Write(ref _writeFailure,
                    Clean($"{exception.GetType().Name}: {exception.Message}"));
            }
        }
    }

    private StreamWriter OpenWriter()
    {
        return new StreamWriter(new FileStream(
            FilePath, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete),
            new UTF8Encoding(false)) { AutoFlush = true };
    }

    private void WriteLine(ref StreamWriter? writer, string entry)
    {
        writer ??= OpenWriter();
        int bytes = Encoding.UTF8.GetByteCount(entry) + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (writer.BaseStream.Length > 0 && writer.BaseStream.Length + bytes > _maxFileBytes)
        {
            writer.Dispose();
            for (int index = BackupCount; index >= 2; index--)
            {
                string previous = BackupPath(index - 1);
                if (File.Exists(previous))
                    File.Move(previous, BackupPath(index), overwrite: true);
            }
            File.Move(FilePath, BackupPath(1), overwrite: true);
            writer = OpenWriter();
        }
        writer.WriteLine(entry);
    }

    private string BackupPath(int index) => Path.Combine(
        DirectoryPath, $"GHMR-controller.{index}.log");

    private static bool IsFileFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or System.Security.SecurityException or
        ArgumentException or NotSupportedException;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;
        _entries.Writer.TryComplete();
        // Drain ordinary buffered writes on exit, with a bound for a failing disk.
        try { _writerTask.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException exception)
        {
            Volatile.Write(ref _writeFailure, Clean(exception.GetBaseException().Message));
        }
    }
}
