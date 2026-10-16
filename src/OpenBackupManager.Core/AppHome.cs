using System.Security.Cryptography;
using System.Text;

namespace OpenBackupManager.Core;

/// <summary>
/// The folder a copy of the app keeps everything in, when it keeps it all in one place. Copies with different homes run
/// side by side
/// </summary>
public static class AppHome
{
    // --home command line arg, then OBM_HOME env var. Null when neither is given, which leaves it to the app's default
    public static string? Chosen(string? argument, string? environment)
    {
        if (!string.IsNullOrEmpty(argument))
        {
            return Path.GetFullPath(argument);
        }

        return string.IsNullOrEmpty(environment) ? null : Path.GetFullPath(environment);
    }

    // Windows keeps everything in one folder anyway. A copy that wasn't installed, such as a build run while developing,
    // gets a folder of its own so it never shares data with the installed app
    public static string WindowsDefault(bool installed, string localAppData) =>
        installed ? InstalledHome(localAppData) : Path.Combine(localAppData, "OpenBackupManager-Dev");

    // Only the installed app's own home starts at login without being asked, not dev builds or other homes. The app
    // passes its home on when it restarts, so this goes by the folder rather than how it was chosen
    public static bool StartsAtLoginByDefault(string home, bool installed, string localAppData) =>
        installed && Normalize(home) == Normalize(InstalledHome(localAppData));

    // Names what each home has its own of on Windows, such as its locks, so each home has one copy running
    public static string InstanceKey(string home) =>
        "OpenBackupManager-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(home))))[..16];

    private static string InstalledHome(string localAppData) => Path.Combine(localAppData, "OpenBackupManager");

    // So the same folder written differently is the same home, ignoring case as Windows does
    private static string Normalize(string home) =>
        Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
}
