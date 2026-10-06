using Ghmr.Controller.Launch;
using Ghmr.Core.Launch;

internal static class CompletedClosePolicySelfTests
{
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "GhmrTest", "GTA5_Enhanced.exe");
        GameLaunchProfile profile = new()
        {
            Game = "gtav_enhanced", ProcessName = "GTA5_Enhanced", LaunchExecutablePath = path
        };
        Require(CompletedGameClosePolicy.CanTerminateGtaV(profile, "GTA5_Enhanced", path),
            "The configured GTA V gameplay executable must be eligible after an accepted pass.");
        Require(!CompletedGameClosePolicy.CanTerminateGtaV(profile, "Steam", path) &&
            !CompletedGameClosePolicy.CanTerminateGtaV(profile, "Launcher", path) &&
            !CompletedGameClosePolicy.CanTerminateGtaV(profile, "GTA5", path),
            "Launchers and other game editions must never be termination targets.");
        Require(!CompletedGameClosePolicy.CanTerminateGtaV(profile, "GTA5_Enhanced", null) &&
            !CompletedGameClosePolicy.CanTerminateGtaV(profile, "GTA5_Enhanced",
                Path.Combine(Path.GetTempPath(), "OtherInstallation", "GTA5_Enhanced.exe")),
            "Unknown executable identity and another installation must reject.");
        Require(!CompletedGameClosePolicy.CanTerminateGtaV(profile with { Game = "gta3de" }, "GTA5_Enhanced", path) &&
            !CompletedGameClosePolicy.CanTerminateGtaV(profile with { ProcessName = "Launcher" }, "GTA5_Enhanced", path) &&
            !CompletedGameClosePolicy.CanTerminateGtaV(profile with
                { LaunchExecutablePath = Path.Combine(Path.GetTempPath(), "Launcher.exe") }, "GTA5_Enhanced", path),
            "Misconfigured game, process or launch executable must reject.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
