using Ghmr.Core.Launch;
using System.Text.Json;

namespace Ghmr.Controller.Launch;

public sealed class GameLaunchProfileStore : IGameLaunchProfileProvider
{
    private const int CurrentSchemaVersion = 1;
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, GameLaunchProfile> _profiles;

    public GameLaunchProfileStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "game-launch-profiles.json");
        _profiles = Load();
    }

    public GameLaunchProfile? Find(string game)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        lock (_gate)
        {
            return _profiles.TryGetValue(game, out GameLaunchProfile? profile)
                ? profile
                : null;
        }
    }

    public void SetFromExecutable(
        string game,
        string executablePath,
        string? processName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath) ||
            !string.Equals(
                Path.GetExtension(fullPath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Select an existing Windows game executable.");
        }

        string normalizedProcessName = string.IsNullOrWhiteSpace(processName)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : Path.GetFileNameWithoutExtension(processName.Trim());
        GameLaunchProfile profile = new()
        {
            Game = game,
            LaunchExecutablePath = fullPath,
            ProcessName = normalizedProcessName
        };
        profile.Validate();

        lock (_gate)
        {
            _profiles[game] = profile;
            Save();
        }
    }

    private Dictionary<string, GameLaunchProfile> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, GameLaunchProfile>(StringComparer.Ordinal);
        }

        try
        {
            ProfileDocument? document = JsonSerializer.Deserialize<ProfileDocument>(
                File.ReadAllText(_path),
                JsonOptions);
            if (document is null || document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException("The game launch profile file has an unsupported format.");
            }

            Dictionary<string, GameLaunchProfile> result =
                new(StringComparer.Ordinal);
            foreach (GameLaunchProfile profile in document.Profiles)
            {
                profile.Validate();
                result[profile.Game] = profile;
            }
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The game launch profile file is malformed.", exception);
        }
    }

    private void Save()
    {
        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        ProfileDocument document = new(
            CurrentSchemaVersion,
            _profiles.Values.OrderBy(profile => profile.Game, StringComparer.Ordinal).ToArray());
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record ProfileDocument(
        int SchemaVersion,
        IReadOnlyList<GameLaunchProfile> Profiles);
}
