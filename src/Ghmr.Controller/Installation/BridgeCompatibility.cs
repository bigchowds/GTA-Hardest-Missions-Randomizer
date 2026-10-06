using Ghmr.Core.Launch;
using System.Text.RegularExpressions;

namespace Ghmr.Controller.Installation;

public sealed record BridgeCompatibilityResult(
    string Game,
    string RequiredVersion,
    string? InstalledVersion,
    string ScriptPath,
    string Problem)
{
    public bool IsCompatible => Problem.Length == 0;
}

public static partial class BridgeCompatibility
{
    public const string RequiredIniTransportMarker =
        "// GHMR INI transport v0.2.0-verified-event-writes";

    private sealed record BridgeSpecification(
        string RequiredVersion,
        string ScriptRelativePath);

    private static readonly IReadOnlyDictionary<string, BridgeSpecification> Specifications =
        new Dictionary<string, BridgeSpecification>(StringComparer.Ordinal)
        {
            ["sade"] = new("0.1.4", Path.Combine("CLEO", "ghmr_sa_bridge[fs].js")),
            ["gta3de"] = new("0.1.5", Path.Combine("CLEO", "ghmr_gta3_bridge[fs].js")),
            ["vcde"] = new("0.1.7", Path.Combine("CLEO", "ghmr_vc_bridge[fs].js")),
            ["gta4"] = new(
                "0.1.17-retained-event-delivery",
                Path.Combine("scripts", "ghmr_gta4_bridge.cs")),
            ["gtav_enhanced"] = new(
                "0.1.13-foreground-restore-confirm",
                Path.Combine("scripts", "ghmr_gtav_bridge.3.cs"))
        };

    public static string RequiredVersion(string game)
    {
        return GetSpecification(game).RequiredVersion;
    }

    public static bool IsReportedVersionCompatible(
        string game,
        string? reportedVersion,
        out string requiredVersion)
    {
        requiredVersion = RequiredVersion(game);
        return string.Equals(reportedVersion, requiredVersion, StringComparison.Ordinal);
    }

    public static BridgeCompatibilityResult Inspect(GameLaunchProfile? profile, string game)
    {
        BridgeSpecification specification = GetSpecification(game);
        if (profile is null)
        {
            return new BridgeCompatibilityResult(
                game,
                specification.RequiredVersion,
                null,
                string.Empty,
                "The game path is not configured.");
        }

        if (!File.Exists(profile.LaunchExecutablePath))
        {
            return new BridgeCompatibilityResult(
                game,
                specification.RequiredVersion,
                null,
                string.Empty,
                "The configured game executable does not exist.");
        }

        string gameDirectory = Path.GetDirectoryName(profile.LaunchExecutablePath)
            ?? string.Empty;
        string scriptPath = Path.Combine(gameDirectory, specification.ScriptRelativePath);
        if (!File.Exists(scriptPath))
        {
            return new BridgeCompatibilityResult(
                game,
                specification.RequiredVersion,
                null,
                scriptPath,
                "The GHMR bridge is not installed.");
        }

        try
        {
            string source = File.ReadAllText(scriptPath);
            string? installedVersion = ExtractVersion(source);
            if (installedVersion is null)
            {
                return new BridgeCompatibilityResult(
                    game,
                    specification.RequiredVersion,
                    null,
                    scriptPath,
                    "The installed bridge has no readable version marker.");
            }

            if (!string.Equals(
                    installedVersion,
                    specification.RequiredVersion,
                    StringComparison.Ordinal))
            {
                return new BridgeCompatibilityResult(
                    game,
                    specification.RequiredVersion,
                    installedVersion,
                    scriptPath,
                    $"Bridge v{installedVersion} is outdated.");
            }

            if ((game is "sade" or "gta3de" or "vcde") &&
                !source.Contains(RequiredIniTransportMarker, StringComparison.Ordinal))
            {
                return new BridgeCompatibilityResult(
                    game,
                    specification.RequiredVersion,
                    installedVersion,
                    scriptPath,
                    "The shared INI transport is outdated. Use Install/Repair Bridges.");
            }

            return new BridgeCompatibilityResult(
                game,
                specification.RequiredVersion,
                installedVersion,
                scriptPath,
                string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new BridgeCompatibilityResult(
                game,
                specification.RequiredVersion,
                null,
                scriptPath,
                "GHMR could not read the installed bridge: " + exception.Message);
        }
    }

    public static void VerifyInstalled(GameLaunchProfile profile)
    {
        BridgeCompatibilityResult result = Inspect(profile, profile.Game);
        if (!result.IsCompatible)
        {
            throw new InvalidDataException(
                $"The bridge copy did not verify. {result.Problem} " +
                $"Required version: {result.RequiredVersion}.");
        }
    }

    private static BridgeSpecification GetSpecification(string game)
    {
        if (!Specifications.TryGetValue(game, out BridgeSpecification? specification))
            throw new ArgumentOutOfRangeException(nameof(game), "Unsupported GHMR game id.");
        return specification;
    }

    private static string? ExtractVersion(string source)
    {
        Match match = VersionMarker().Match(source);
        return match.Success ? match.Groups["version"].Value : null;
    }

    [GeneratedRegex(
        "bridgeVersion[^0-9]{0,32}(?<version>[0-9]+(?:\\.[0-9]+)+(?:[-+._A-Za-z0-9]*))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionMarker();
}
