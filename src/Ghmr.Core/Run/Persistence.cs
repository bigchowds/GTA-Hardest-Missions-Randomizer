using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghmr.Core.Run;

public interface IRunPersistence
{
    RunSnapshot? Load();
    void Save(RunSnapshot snapshot);
}

public interface IAuditSink
{
    void Append(
        string runId,
        long sequence,
        DateTimeOffset timestampUtc,
        string eventName,
        object? details = null);
}

public sealed class FileRunPersistence : IRunPersistence
{
    private readonly string _statePath;

    public FileRunPersistence(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _statePath = Path.Combine(dataDirectory, "active-run.json");
    }

    public RunSnapshot? Load()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        string json = File.ReadAllText(_statePath);
        return JsonSerializer.Deserialize<RunSnapshot>(json, JsonOptions);
    }

    public void Save(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? directory = Path.GetDirectoryName(_statePath);
        if (directory is null)
        {
            throw new InvalidOperationException("Run-state path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_statePath)}.{Guid.NewGuid():N}.tmp");

        RunSnapshot persistedSnapshot = CreatePersistedSnapshot(snapshot);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(persistedSnapshot, JsonOptions);
        using (FileStream stream = new(
                   temporaryPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(json);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }

    private static RunSnapshot CreatePersistedSnapshot(RunSnapshot source)
    {
        RunSnapshot persisted = source.Copy();
        persisted.InternalSeed = 0;

        if (persisted.Phase != RunPhase.Finished)
        {
            for (int index = 0; index < persisted.MissionOrder.Count; index++)
            {
                if (index > persisted.CurrentIndex)
                {
                    persisted.MissionOrder[index] = "hidden";
                }
            }
        }

        return persisted;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class FileAuditSink : IAuditSink
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        FileRunPersistence.JsonOptions)
    {
        WriteIndented = false
    };

    private readonly string _runsDirectory;
    private readonly object _sync = new();

    public FileAuditSink(string dataDirectory)
    {
        _runsDirectory = Path.Combine(dataDirectory, "runs");
        Directory.CreateDirectory(_runsDirectory);
    }

    public void Append(
        string runId,
        long sequence,
        DateTimeOffset timestampUtc,
        string eventName,
        object? details = null)
    {
        if (!Guid.TryParseExact(runId, "N", out _))
        {
            throw new InvalidDataException("Run id is not a canonical identifier.");
        }

        string path = Path.Combine(_runsDirectory, $"{runId}.jsonl");
        byte[] line = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    runId,
                    sequence,
                    timestampUtc,
                    eventName,
                    details
                },
                AuditJsonOptions) + Environment.NewLine);

        lock (_sync)
        {
            using FileStream stream = new(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough);
            stream.Write(line);
            stream.Flush(flushToDisk: true);
        }
    }
}
