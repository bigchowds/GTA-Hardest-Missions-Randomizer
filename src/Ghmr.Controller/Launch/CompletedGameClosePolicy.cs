using Ghmr.Core.Launch;

namespace Ghmr.Controller.Launch;

public static class CompletedGameClosePolicy
{
    public static bool CanTerminateGtaV(
        GameLaunchProfile profile, string processName, string? actualExecutablePath)
    {
        // Do not act on a launcher, similarly named process or a different
        // installation. Refuse unverifiable identity instead of guessing.
        if (profile.Game != "gtav_enhanced" ||
            !profile.ProcessName.Equals("GTA5_Enhanced", StringComparison.OrdinalIgnoreCase) ||
            !processName.Equals("GTA5_Enhanced", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(actualExecutablePath) ||
            !Path.GetFileName(profile.LaunchExecutablePath).Equals("GTA5_Enhanced.exe", StringComparison.OrdinalIgnoreCase))
            return false;
        return Path.GetFullPath(profile.LaunchExecutablePath).Equals(
            Path.GetFullPath(actualExecutablePath), StringComparison.OrdinalIgnoreCase);
    }
}
