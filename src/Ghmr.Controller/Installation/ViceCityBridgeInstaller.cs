namespace Ghmr.Controller.Installation;

public sealed record ViceCityBridgeInstallation(
    string GameDirectory,
    string ScriptPath,
    string TransportPath,
    bool DefinitionsChanged);

public static class ViceCityBridgeInstaller
{
    private const string ExecutableName = "ViceCity.exe";

    public static ViceCityBridgeInstallation Install(string executablePath)
    {
        CleoBridgeInstallation installed = CleoBridgeInstaller.Install(
            executablePath,
            ExecutableName,
            "vcde",
            "ghmr_vc_bridge.js");

        // Remove the two read-only probes used before the controller bridge was
        // implemented. Running them beside the bridge creates duplicate and
        // potentially misleading mission-state log lines.
        string cleoDirectory = Path.Combine(installed.GameDirectory, "CLEO");
        foreach (string legacyProbe in new[]
        {
            "ghmr_vc_probe.js",
            "ghmr_vc_mission_state_probe.js"
        })
        {
            string path = Path.Combine(cleoDirectory, legacyProbe);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return new ViceCityBridgeInstallation(
            installed.GameDirectory,
            installed.ScriptPath,
            installed.TransportPath,
            installed.DefinitionsChanged);
    }
}
