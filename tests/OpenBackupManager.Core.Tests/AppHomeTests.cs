using System.Runtime.InteropServices;

namespace OpenBackupManager.Core.Tests;

public class AppHomeTests
{
    private const string LocalAppData = @"C:\Users\Test\AppData\Local";

    private static readonly string HomeA = Path.Combine(Path.GetTempPath(), "obm-home-a");
    private static readonly string HomeB = Path.Combine(Path.GetTempPath(), "obm-home-b");

    [Test]
    public void Chosen_NeitherGiven_LeavesItToTheDefault() =>
        Assert.That(AppHome.Chosen(null, null), Is.Null);

    [Test]
    public void Chosen_Environment() =>
        Assert.That(AppHome.Chosen(null, HomeA), Is.EqualTo(HomeA));

    [Test]
    public void Chosen_ArgumentOverridesTheEnvironment() =>
        Assert.That(AppHome.Chosen(HomeB, HomeA), Is.EqualTo(HomeB));

    [Test]
    public void Chosen_RelativeIsMadeFull() =>
        Assert.That(AppHome.Chosen("obm-home", null), Is.EqualTo(Path.GetFullPath("obm-home")));

    [Test]
    [Platform("Win")]
    public void WindowsDefault_Installed_UsesTheAppFolder() =>
        Assert.That(AppHome.WindowsDefault(installed: true, LocalAppData), Is.EqualTo(@"C:\Users\Test\AppData\Local\OpenBackupManager"));

    // The app keeps everything in its home, which for an installed copy is Windows' own layout
    [Test]
    [Platform("Win")]
    public void WindowsDefault_Installed_MatchesTheWindowsLayout() =>
        Assert.That(
            AppPaths.InHome(AppHome.WindowsDefault(installed: true, LocalAppData)),
            Is.EqualTo(AppPaths.ForOS(OSPlatform.Windows, name => name == "LOCALAPPDATA" ? LocalAppData : null)));

    [Test]
    [Platform("Win")]
    public void WindowsDefault_NotInstalled_UsesAFolderOfItsOwn() =>
        Assert.That(AppHome.WindowsDefault(installed: false, LocalAppData), Is.EqualTo(@"C:\Users\Test\AppData\Local\OpenBackupManager-Dev"));

    [Test]
    public void CommandLinePaths_Argument_KeepsEverythingThere() =>
        Assert.That(AppHome.CommandLinePaths(HomeA, installed: () => false), Is.EqualTo(AppPaths.InHome(HomeA)));

    [TestCase(@"C:\Users\Test\AppData\Local\OpenBackupManager")]
    [TestCase(@"c:\users\test\appdata\local\openbackupmanager\")]
    [Platform("Win")]
    public void StartsAtLoginByDefault_TheInstalledAppsOwnHome(string home) =>
        Assert.That(AppHome.StartsAtLoginByDefault(home, installed: true, LocalAppData), Is.True);

    [Test]
    [Platform("Win")]
    public void StartsAtLoginByDefault_NotAnotherHome() =>
        Assert.That(AppHome.StartsAtLoginByDefault(@"D:\Homes\A", installed: true, LocalAppData), Is.False);

    [Test]
    [Platform("Win")]
    public void StartsAtLoginByDefault_NotACopyThatWasntInstalled() =>
        Assert.That(AppHome.StartsAtLoginByDefault(AppHome.WindowsDefault(installed: false, LocalAppData), installed: false, LocalAppData), Is.False);

    [Test]
    [Platform("Win")]
    public void InstanceKey_IsTheSameForTheSameFolderWrittenDifferently() =>
        Assert.That(AppHome.InstanceKey(@"d:\homes\a\"), Is.EqualTo(AppHome.InstanceKey(@"D:\Homes\A")));

    [Test]
    public void InstanceKey_DiffersBetweenFolders() =>
        Assert.That(AppHome.InstanceKey(HomeA), Is.Not.EqualTo(AppHome.InstanceKey(HomeB)));
}
