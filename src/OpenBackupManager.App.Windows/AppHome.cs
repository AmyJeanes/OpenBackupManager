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

        return Path.Combine(localAppData, installed ? "OpenBackupManager" : "OpenBackupManager-Dev");
    }

    // Names the single-instance lock, so each home has one copy running
    public static string InstanceKey(string home)
    {
        var path = Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return "OpenBackupManager-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
    }
}
