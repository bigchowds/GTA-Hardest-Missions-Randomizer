namespace Ghmr.Controller.Installation;

public sealed record Gta5BridgeInstallation(
    string GameDirectory,
    string ScriptPath,
    string TransportPath,
    bool DefinitionsChanged);

public static class Gta5BridgeInstaller
{
    private const string ExecutableName = "GTA5_Enhanced.exe";

    public static Gta5BridgeInstallation Install(string executablePath)
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
                "Select GTA5_Enhanced.exe from the GTA V Enhanced game folder.");
        }

        string gameDirectory = Path.GetDirectoryName(fullExecutablePath)
            ?? throw new InvalidDataException(
                "The selected GTA5_Enhanced.exe has no parent folder.");
        string[] requiredRuntimeFiles =
        [
            "ScriptHookV.dll",
            "ScriptHookVDotNet.asi",
            "ScriptHookVDotNet3.dll"
        ];
        string? missingRuntime = requiredRuntimeFiles.FirstOrDefault(
            fileName => !File.Exists(Path.Combine(gameDirectory, fileName)));
        if (missingRuntime is not null)
        {
            throw new InvalidDataException(
                $"GTA V Enhanced ScriptHookVDotNet is not ready: {missingRuntime} " +
                "is missing beside GTA5_Enhanced.exe. Install the same compatible " +
                "runtime used by the successful Derailed probe, then try again.");
        }

        string sourceScript = Path.Combine(
            AppContext.BaseDirectory,
            "adapters",
            "gtav_enhanced",
            "ghmr_gtav_bridge.3.cs");
        if (!File.Exists(sourceScript))
        {
            throw new FileNotFoundException(
                "This controller package is missing its readable GTA V " +
                "ScriptHookVDotNet bridge. Use a complete CI release package.",
                sourceScript);
        }

        string scriptsDirectory = Path.Combine(gameDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);
        string targetScript = Path.Combine(
            scriptsDirectory,
            "ghmr_gtav_bridge.3.cs");
        CopyAtomically(sourceScript, targetScript);

        // Remove only GHMR's superseded read-only probes and earlier bridge
        // filenames. Third-party trainers and ScriptHook files are untouched.
        string[] supersededFiles =
        [
            "ghmr_gtav_derailed_probe.3.cs",
            "ghmr_gtav_probe.3.cs",
            "ghmr_gtav_bridge.cs"
        ];
        foreach (string fileName in supersededFiles)
        {
            DeleteIfPresent(Path.Combine(scriptsDirectory, fileName));
        }

        string ipcDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR",
            "controller",
            "ipc");
        Directory.CreateDirectory(ipcDirectory);

        return new Gta5BridgeInstallation(
            gameDirectory,
            targetScript,
            ipcDirectory,
            DefinitionsChanged: false);
    }

    private static void CopyAtomically(string source, string destination)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "GTA V bridge destination has no parent folder.");
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
