using System.Text.Json;

namespace Ghmr.Core;

public sealed record MissionDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Game { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Variant { get; init; } = string.Empty;
    public string BridgeStatus { get; init; } = string.Empty;
    public int? LaunchIndex { get; init; }
    public string? LaunchScript { get; init; }
}

public sealed class MissionCatalog
{
    private static readonly HashSet<string> SupportedGames = new(StringComparer.Ordinal)
    {
        "gta3de",
        "vcde",
        "sade",
        "gta4",
        "gtav_enhanced"
    };

    public int SchemaVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public List<MissionDefinition> Missions { get; init; } = [];

    public static MissionCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        MissionCatalog catalog = JsonSerializer.Deserialize<MissionCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("The mission catalog is empty.");

        catalog.Validate();
        return catalog;
    }

    public MissionDefinition GetMission(string id)
    {
        return Missions.FirstOrDefault(mission =>
                   string.Equals(mission.Id, id, StringComparison.Ordinal))
               ?? throw new InvalidDataException($"Unknown mission id: {id}");
    }

    public void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported mission schema: {SchemaVersion}");
        }

        if (Missions.Count == 0)
        {
            throw new InvalidDataException("The mission catalog contains no missions.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (MissionDefinition mission in Missions)
        {
            if (string.IsNullOrWhiteSpace(mission.Id) ||
                string.IsNullOrWhiteSpace(mission.Game) ||
                string.IsNullOrWhiteSpace(mission.Title))
            {
                throw new InvalidDataException("Every mission requires an id, game and title.");
            }

            if (!ids.Add(mission.Id))
            {
                throw new InvalidDataException($"Duplicate mission id: {mission.Id}");
            }

            if (!SupportedGames.Contains(mission.Game))
            {
                throw new InvalidDataException($"Unsupported game id: {mission.Game}");
            }

            if (mission.LaunchIndex < 0)
            {
                throw new InvalidDataException(
                    $"Mission {mission.Id} has an invalid launch index.");
            }

            if (mission.LaunchScript is not null &&
                string.IsNullOrWhiteSpace(mission.LaunchScript))
            {
                throw new InvalidDataException(
                    $"Mission {mission.Id} has an empty launch script.");
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
