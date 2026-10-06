namespace Ghmr.Controller.Installation;

public sealed record SanAndreasBridgeInstallation(
    string GameDirectory,
    string ScriptPath,
    string TransportPath,
    bool DefinitionsChanged);

public static class SanAndreasBridgeInstaller
{
    private const string ExecutableName = "SanAndreas.exe";

    public static SanAndreasBridgeInstallation Install(string executablePath)
    {
        CleoBridgeInstallation installed = CleoBridgeInstaller.Install(
            executablePath,
            ExecutableName,
            "sade",
            "ghmr_sa_bridge.js");
        return new SanAndreasBridgeInstallation(
            installed.GameDirectory,
            installed.ScriptPath,
            installed.TransportPath,
            installed.DefinitionsChanged);
    }
}
