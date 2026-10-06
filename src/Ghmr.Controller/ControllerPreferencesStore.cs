using System.Text.Json;

namespace Ghmr.Controller;

public sealed record ControllerPreferences
{
    public bool TransitionMusicEnabled { get; init; } = true;
    public bool FixedTestOrderEnabled { get; init; }
    public string[] FixedMissionOrder { get; init; } = BetaRunChoices.MissionIds.ToArray();
    public bool ViceCityAutoResumeEnabled { get; init; } = true;
}

public static class BetaRunChoices
{
    public static IReadOnlyList<string> MissionIds { get; } = Array.AsReadOnly(new[]
    {
        "sa.wrong_side_of_the_tracks", "gta3.espresso_2_go", "vc.demolition_man",
        "gta4.three_leaf_clover", "gtav.derailed"
    });

    public static string Label(string id) => id switch
    {
        "sa.wrong_side_of_the_tracks" => "San Andreas — Wrong Side of the Tracks",
        "gta3.espresso_2_go" => "GTA III — Espresso-2-Go!",
        "vc.demolition_man" => "Vice City — Demolition Man",
        "gta4.three_leaf_clover" => "GTA IV — Three Leaf Clover",
        "gtav.derailed" => "GTA V — Derailed",
        _ => throw new ArgumentException("Unknown beta mission.", nameof(id))
    };

    public static string[] ValidateFixedOrder(IEnumerable<string>? ids)
    {
        string[] order = ids?.ToArray() ?? [];
        if (order.Length == 0 || order.Distinct(StringComparer.Ordinal).Count() != order.Length ||
            order.Any(id => !MissionIds.Contains(id, StringComparer.Ordinal)))
            throw new ArgumentException("Select at least one mission, with no duplicates or unknown missions.");
        return order;
    }
}

public sealed class ControllerPreferencesStore
{
    private readonly string _path;

    public ControllerPreferencesStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        _path = Path.Combine(dataDirectory, "preferences.json");
        Current = Load();
    }

    public string DataDirectory { get; }

    public ControllerPreferences Current { get; private set; }

    public void Save(ControllerPreferences preferences)
    {
        BetaRunChoices.ValidateFixedOrder(preferences.FixedMissionOrder);
        Directory.CreateDirectory(DataDirectory);
        string temporaryPath = _path + ".tmp";
        string json = JsonSerializer.Serialize(
            preferences,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _path, overwrite: true);
        Current = preferences;
    }

    private ControllerPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new ControllerPreferences();
            }

            ControllerPreferences preferences = JsonSerializer.Deserialize<ControllerPreferences>(
                       File.ReadAllText(_path)) ??
                   new ControllerPreferences();
            BetaRunChoices.ValidateFixedOrder(preferences.FixedMissionOrder);
            return preferences;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or ArgumentException)
        {
            return new ControllerPreferences();
        }
    }
}
