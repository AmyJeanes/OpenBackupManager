using System.Security.Cryptography;
using System.Text;

namespace OpenBackupManager.App.Windows;

// The folder everything this copy of the app keeps goes in. Copies with different homes run side by side
internal static class AppHome
{
    // --home command line arg, then OBM_HOME env var, then the installed app's folder. A copy that wasn't installed,
    //  such as a build run while developing, gets a folder of its own so it never shares data with the installed app
    public static string Resolve(IReadOnlyList<string> args, string? environment, bool installed, string localAppData)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--home")
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        if (!string.IsNullOrEmpty(environment))
        {
            return Path.GetFullPath(environment);
        }

        return installed ? InstalledHome(localAppData) : Path.Combine(localAppData, "OpenBackupManager-Dev");
    }

    // Only the installed app's own home starts at login without being asked, not dev builds or other homes. The app
    // passes its home on when it restarts, so this goes by the folder rather than how it was chosen
    public static bool StartsAtLoginByDefault(string home, bool installed, string localAppData) =>
        installed && Normalize(home) == Normalize(InstalledHome(localAppData));

    // Names the single-instance lock, so each home has one copy running
    public static string InstanceKey(string home) =>
        "OpenBackupManager-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(home))))[..16];

    private static string InstalledHome(string localAppData) => Path.Combine(localAppData, "OpenBackupManager");

    // So the same folder written differently is the same home
    private static string Normalize(string home) =>
        Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
}
