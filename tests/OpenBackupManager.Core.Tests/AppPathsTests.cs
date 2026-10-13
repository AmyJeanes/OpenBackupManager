using System.Runtime.InteropServices;

namespace OpenBackupManager.Core.Tests;

public class AppPathsTests
{
    [Test]
    public void Windows_KeepsEverythingInLocalAppData() =>
        Assert.That(
            AppPaths.ForOS(OSPlatform.Windows, Environment(("LOCALAPPDATA", @"C:\Users\Amy\AppData\Local"))),
            Is.EqualTo(new AppPaths(
                @"C:\Users\Amy\AppData\Local\OpenBackupManager",
                @"C:\Users\Amy\AppData\Local\OpenBackupManager",
                @"C:\Users\Amy\AppData\Local\OpenBackupManager\logs",
                @"C:\Users\Amy\AppData\Local\OpenBackupManager\cache")));

    [Test]
    public void MacOS_KeepsConfigInDotConfigAndTheRestInLibrary() =>
        Assert.That(
            AppPaths.ForOS(OSPlatform.OSX, Environment(("HOME", "/Users/amy"))),
            Is.EqualTo(new AppPaths(
                "/Users/amy/.config/openbackupmanager",
                "/Users/amy/Library/Application Support/OpenBackupManager",
                "/Users/amy/Library/Logs/OpenBackupManager",
                "/Users/amy/Library/Caches/OpenBackupManager")));

    [Test]
    public void Linux_UsesTheXdgDefaults() =>
        Assert.That(
            AppPaths.ForOS(OSPlatform.Linux, Environment(("HOME", "/home/amy"))),
            Is.EqualTo(new AppPaths(
                "/home/amy/.config/openbackupmanager",
                "/home/amy/.local/state/openbackupmanager",
                "/home/amy/.local/state/openbackupmanager/logs",
                "/home/amy/.cache/openbackupmanager")));

    [Test]
    public void Linux_FollowsTheXdgVariables() =>
        Assert.That(
            AppPaths.ForOS(OSPlatform.Linux, Environment(
                ("HOME", "/home/amy"),
                ("XDG_CONFIG_HOME", "/data/config/"),
                ("XDG_STATE_HOME", "/data/state"),
                ("XDG_CACHE_HOME", "/data/cache"))),
            Is.EqualTo(new AppPaths(
                "/data/config/openbackupmanager",
                "/data/state/openbackupmanager",
                "/data/state/openbackupmanager/logs",
                "/data/cache/openbackupmanager")));

    [Test]
    public void MacOS_FollowsXdgConfigHomeForConfigOnly()
    {
        var paths = AppPaths.ForOS(OSPlatform.OSX, Environment(("HOME", "/Users/amy"), ("XDG_CONFIG_HOME", "/data/config"), ("XDG_STATE_HOME", "/data/state")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths.Config, Is.EqualTo("/data/config/openbackupmanager"));
            Assert.That(paths.State, Is.EqualTo("/Users/amy/Library/Application Support/OpenBackupManager"));
        }
    }

    [Test]
    public void Xdg_IgnoresRelativePaths() =>
        Assert.That(
            AppPaths.ForOS(OSPlatform.Linux, Environment(("HOME", "/home/amy"), ("XDG_CONFIG_HOME", "config"))).Config,
            Is.EqualTo("/home/amy/.config/openbackupmanager"));

    [Test]
    public void MissingHome_SaysWhichVariable() =>
        Assert.That(() => AppPaths.ForOS(OSPlatform.Linux, Environment()), Throws.InvalidOperationException.With.Message.Contains("HOME"));

    [Test]
    public void InHome_KeepsEverythingInOneFolder()
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-home");
        var paths = AppPaths.InHome(home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths.Config, Is.EqualTo(home));
            Assert.That(paths.State, Is.EqualTo(home));
            Assert.That(paths.Logs, Is.EqualTo(Path.Combine(home, "logs")));
            Assert.That(paths.Cache, Is.EqualTo(Path.Combine(home, "cache")));
            Assert.That(paths.ConfigFile, Is.EqualTo(Path.Combine(home, "config.toml")));
        }
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;
}
