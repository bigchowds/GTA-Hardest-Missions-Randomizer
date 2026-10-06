namespace Ghmr.Controller.Installation;

public sealed record Gta4BridgeInstallation(
    string GameDirectory,
    string ScriptPath,
    string TransportPath,
    bool DefinitionsChanged);

public static class Gta4BridgeInstaller
{
    private const string ExecutableName = "GTAIV.exe";

    public static Gta4BridgeInstallation Install(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath) ||
            !string.Equals(
                Path.GetFileName(fullExecutablePath),
                ExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Select GTAIV.exe from Grand Theft Auto IV\\GTAIV.");
        }

        string gameDirectory = Path.GetDirectoryName(fullExecutablePath)
            ?? throw new InvalidDataException(
                "The selected GTAIV.exe has no parent folder.");
        string scriptHookDotNet = Path.Combine(
            gameDirectory,
            "ScriptHookDotNet.asi");
        string scriptHook = Path.Combine(gameDirectory, "ScriptHook.dll");
        if (!File.Exists(scriptHookDotNet) || !File.Exists(scriptHook))
        {
            throw new InvalidDataException(
                "GTA IV ScriptHookDotNet is not ready. Install compatible " +
                "ScriptHookDotNet.asi and ScriptHook.dll beside GTAIV.exe, " +
                "launch the game once, then try again.");
        }

        string sourceScript = Path.Combine(
            AppContext.BaseDirectory,
            "adapters",
            "gta4",
            "ghmr_gta4_bridge.cs");
        if (!File.Exists(sourceScript))
        {
            throw new FileNotFoundException(
                "This controller package is missing its readable GTA IV " +
                "ScriptHookDotNet bridge. Use a complete CI release package.",
                sourceScript);
        }

        string scriptsDirectory = Path.Combine(gameDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string targetScript = Path.Combine(
            scriptsDirectory,
            "ghmr_gta4_bridge.cs");
        CopyAtomically(sourceScript, targetScript);

        // Remove only superseded GHMR files. Other CLEO and ScriptHookDotNet
        // mods, including trainers, are deliberately left untouched.
        string cleoDirectory = Path.Combine(gameDirectory, "CLEO");
        DeleteIfPresent(Path.Combine(cleoDirectory, "ghmr_gta4_bridge[fs].js"));
        DeleteIfPresent(Path.Combine(cleoDirectory, "ghmr_gta4_bridge.js"));
        DeleteIfPresent(Path.Combine(cleoDirectory, "ghmr_gta4_probe.js"));

        string ipcDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR",
            "controller",
            "ipc");
        Directory.CreateDirectory(ipcDirectory);

        return new Gta4BridgeInstallation(
            gameDirectory,
            targetScript,
            ipcDirectory,
            DefinitionsChanged: false);
    }

    private static void CopyAtomically(string source, string destination)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "GTA IV bridge destination has no parent folder.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, temporaryPath, overwrite: true);
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            DeleteIfPresent(temporaryPath);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
