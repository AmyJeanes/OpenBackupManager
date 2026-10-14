using System.Runtime.InteropServices;
using OpenBackupManager.Core;

namespace OpenBackupManager.App.Windows.Tests;

public class AppHomeTests
{
    private const string LocalAppData = @"C:\Users\Test\AppData\Local";

    [Test]
    public void Installed_UsesTheAppFolder() =>
        Assert.That(AppHome.Resolve([], null, installed: true, LocalAppData), Is.EqualTo(@"C:\Users\Test\AppData\Local\OpenBackupManager"));

    // The app keeps everything in its home, which for an installed copy is Windows' own layout
    [Test]
    public void Installed_MatchesTheWindowsLayout() =>
        Assert.That(
            AppPaths.InHome(AppHome.Resolve([], null, installed: true, LocalAppData)),
            Is.EqualTo(AppPaths.ForOS(OSPlatform.Windows, name => name == "LOCALAPPDATA" ? LocalAppData : null)));

    [Test]
    public void NotInstalled_UsesAFolderOfItsOwn() =>
        Assert.That(AppHome.Resolve([], null, installed: false, LocalAppData), Is.EqualTo(@"C:\Users\Test\AppData\Local\OpenBackupManager-Dev"));

    [Test]
    public void Environment_OverridesTheDefault() =>
        Assert.That(AppHome.Resolve([], @"D:\Homes\A", installed: true, LocalAppData), Is.EqualTo(@"D:\Homes\A"));

    [Test]
    public void HomeArgument_OverridesTheEnvironment() =>
        Assert.That(AppHome.Resolve(["--home", @"D:\Homes\B"], @"D:\Homes\A", installed: true, LocalAppData), Is.EqualTo(@"D:\Homes\B"));

    [TestCase(@"C:\Users\Test\AppData\Local\OpenBackupManager")]
    [TestCase(@"c:\users\test\appdata\local\openbackupmanager\")]
    public void StartsAtLoginByDefault_TheInstalledAppsOwnHome(string home) =>
        Assert.That(AppHome.StartsAtLoginByDefault(home, installed: true, LocalAppData), Is.True);

    [Test]
    public void StartsAtLoginByDefault_NotAnotherHome() =>
        Assert.That(AppHome.StartsAtLoginByDefault(@"D:\Homes\A", installed: true, LocalAppData), Is.False);

    [Test]
    public void StartsAtLoginByDefault_NotACopyThatWasntInstalled() =>
        Assert.That(AppHome.StartsAtLoginByDefault(AppHome.Resolve([], null, installed: false, LocalAppData), installed: false, LocalAppData), Is.False);

    [Test]
    public void InstanceKey_IsTheSameForTheSameFolderWrittenDifferently() =>
        Assert.That(AppHome.InstanceKey(@"d:\homes\a\"), Is.EqualTo(AppHome.InstanceKey(@"D:\Homes\A")));

    [Test]
    public void InstanceKey_DiffersBetweenFolders() =>
        Assert.That(AppHome.InstanceKey(@"D:\Homes\A"), Is.Not.EqualTo(AppHome.InstanceKey(@"D:\Homes\B")));
}
