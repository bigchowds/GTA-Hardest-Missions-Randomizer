namespace Ghmr.Controller.Installation;

public sealed record Gta3BridgeInstallation(
    string GameDirectory,
    string ScriptPath,
    string TransportPath,
    bool DefinitionsChanged);

public static class Gta3BridgeInstaller
{
    private const string ExecutableName = "LibertyCity.exe";

    public static Gta3BridgeInstallation Install(string executablePath)
    {
        CleoBridgeInstallation installed = CleoBridgeInstaller.Install(
            executablePath,
            ExecutableName,
            "gta3de",
            "ghmr_gta3_bridge.js");

        // v0.1.2 briefly supplied a convenience car for Espresso-2-Go!.
        // Normal mode now preserves the original mission setup exactly, so a
        // repair removes that obsolete helper if it was previously installed.
        string legacyVehicleHelper = Path.Combine(
            installed.GameDirectory,
            "CLEO",
            "GHMR",
            "gta3_start_vehicle.js");
        if (File.Exists(legacyVehicleHelper))
        {
            File.Delete(legacyVehicleHelper);
        }

        // This read-only probe was distributed during development and is
        // superseded by the controller bridge. Leaving it installed produces
        // duplicate mission telemetry and confusing heartbeat lines.
        string legacyProbe = Path.Combine(
            installed.GameDirectory,
            "CLEO",
            "ghmr_gta3_outcome_probe.js");
        if (File.Exists(legacyProbe))
        {
            File.Delete(legacyProbe);
        }

        return new Gta3BridgeInstallation(
            installed.GameDirectory,
            installed.ScriptPath,
            installed.TransportPath,
            installed.DefinitionsChanged);
    }
}
