using Ghmr.Core.Installation;

namespace Ghmr.Controller.Installation;

internal sealed record CleoBridgeInstallation(
    string GameDirectory,
    string ScriptPath,
    string TransportPath,
    bool DefinitionsChanged);

internal static class CleoBridgeInstaller
{
    public static CleoBridgeInstallation Install(
        string executablePath,
        string expectedExecutableName,
        string adapterDirectoryName,
        string scriptFileName,
        string executableLocationHint = "Gameface\\Binaries\\Win64",
        string runtimeFileName = "cleo_redux64.asi",
        string definitionsFileName = "unknown_x64.json",
        string iniPluginFileName = "IniFiles64.cleo")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath) ||
            !string.Equals(
                Path.GetFileName(fullExecutablePath),
                expectedExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Select {expectedExecutableName} from {executableLocationHint}.");
        }

        string gameDirectory = Path.GetDirectoryName(fullExecutablePath)
            ?? throw new InvalidDataException("The selected executable has no parent folder.");
        string cleoDirectory = Path.Combine(gameDirectory, "CLEO");
        string cleoRuntime = Path.Combine(gameDirectory, runtimeFileName);
        string definitionsPath = Path.Combine(
            cleoDirectory,
            ".config",
            definitionsFileName);

        if (!File.Exists(cleoRuntime) || !Directory.Exists(cleoDirectory))
        {
            throw new InvalidDataException(
                $"CLEO Redux ({runtimeFileName}) is not installed beside the selected {expectedExecutableName}.");
        }
        if (!File.Exists(definitionsPath))
        {
            throw new InvalidDataException(
                $"CLEO's {definitionsFileName} is missing. Launch the game once with CLEO Redux, close it, then try again.");
        }

        string adapterDirectory = Path.Combine(AppContext.BaseDirectory, "adapters");
        string sourceTransport = Path.Combine(adapterDirectory, "cleo", "ghmr_ini_transport.js");
        string sourceDefinitions = Path.Combine(
            adapterDirectory,
            "cleo",
            "ghmr-ini-extension.v1.json");
        string sourceScript = Path.Combine(
            adapterDirectory,
            adapterDirectoryName,
            scriptFileName);
        EnsurePackagedFile(sourceTransport);
        EnsurePackagedFile(sourceDefinitions);
        EnsurePackagedFile(sourceScript);

        string pluginsDirectory = Path.Combine(cleoDirectory, "CLEO_PLUGINS");
        if (!File.Exists(Path.Combine(pluginsDirectory, iniPluginFileName)))
            throw new InvalidDataException($"CLEO Redux's {iniPluginFileName} plugin is missing. Reinstall CLEO Redux with the IniFiles extension selected.");
        string targetScript = Path.Combine(cleoDirectory,
            Path.GetFileNameWithoutExtension(scriptFileName) + "[fs].js");
        string ipcDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR", "controller", "ipc");
        Directory.CreateDirectory(ipcDirectory);
        if (System.Text.Encoding.UTF8.GetByteCount(
                Path.Combine(ipcDirectory, adapterDirectoryName + "-controller.ini")) > 120)
            throw new InvalidDataException("The Windows account path is too long for the CLEO INI bridge. Choose a shorter account path before installation.");

        string transport = File.ReadAllText(sourceTransport)
            .Replace("__GHMR_IPC_DIRECTORY__",
                System.Text.Json.JsonSerializer.Serialize(ipcDirectory), StringComparison.Ordinal)
            .Replace("__GHMR_GAME_ID__",
                System.Text.Json.JsonSerializer.Serialize(adapterDirectoryName), StringComparison.Ordinal);
        string installedScript = transport + "\n" + File.ReadAllText(sourceScript);

        bool definitionsChanged = CleoDefinitionPatcher.Apply(
            definitionsPath,
            sourceDefinitions);
        WriteAtomically(installedScript, targetScript);

        // Remove only files installed by GHMR's previous blocked transport.
        string oldPlugin = Path.Combine(
            pluginsDirectory,
            runtimeFileName.Contains("64", StringComparison.OrdinalIgnoreCase)
                ? "GHMR.CleoBridge64.cleo"
                : "GHMR.CleoBridge.cleo");
        string oldScript = Path.Combine(cleoDirectory, scriptFileName);
        if (File.Exists(oldPlugin)) File.Delete(oldPlugin);
        if (File.Exists(oldScript)) File.Delete(oldScript);

        return new CleoBridgeInstallation(
            gameDirectory,
            targetScript,
            ipcDirectory,
            definitionsChanged);
    }

    private static void EnsurePackagedFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "This controller package is missing its readable CLEO bridge files. Use a complete CI release package.",
                path);
        }
    }

    private static void WriteAtomically(string source, string destination)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Adapter destination has no parent folder.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, source);
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
