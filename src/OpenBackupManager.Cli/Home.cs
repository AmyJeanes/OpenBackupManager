using OpenBackupManager.Core;
using Velopack.Locators;

namespace OpenBackupManager.Cli;

internal static class Home
{
    // --home, then OBM_HOME, then the OS's usual folders. On Windows that's the same home the tray app picks, so a dev
    // build of obm works with a dev build of the app
    public static AppPaths Paths(string? argument)
    {
        var home = AppHome.Chosen(argument, Environment.GetEnvironmentVariable("OBM_HOME"));
        if (home is null && OperatingSystem.IsWindows())
        {
            home = AppHome.WindowsDefault(Installed(), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        return home is null ? AppPaths.ForThisOS() : AppPaths.InHome(home);
    }

    // Velopack installs obm beside the app, so it finds the same install
    private static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
}
