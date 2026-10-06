namespace OpenBackupManager.App.Windows.Tests;

public class AppHomeTests
{
    private const string LocalAppData = @"C:\Users\Test\AppData\Local";

    [Test]
    public void Installed_UsesTheAppFolder() =>
        Assert.That(AppHome.Resolve([], null, installed: true, LocalAppData), Is.EqualTo(@"C:\Users\Test\AppData\Local\OpenBackupManager"));

    [Test]
    public void NotInstalled_UsesAFolderOfItsOwn() =>
        Assert.That(AppHome.Resolve([], null, installed: false, LocalAppData), Is.EqualTo(@"C:\Users\Test\AppData\Local\OpenBackupManager-Dev"));

    [Test]
    public void Environment_OverridesTheDefault() =>
        Assert.That(AppHome.Resolve([], @"D:\Homes\A", installed: true, LocalAppData), Is.EqualTo(@"D:\Homes\A"));

    [Test]
    public void HomeArgument_OverridesTheEnvironment() =>
        Assert.That(AppHome.Resolve(["--home", @"D:\Homes\B"], @"D:\Homes\A", installed: true, LocalAppData), Is.EqualTo(@"D:\Homes\B"));

    [Test]
    public void InstanceKey_IsTheSameForTheSameFolderWrittenDifferently() =>
        Assert.That(AppHome.InstanceKey(@"d:\homes\a\"), Is.EqualTo(AppHome.InstanceKey(@"D:\Homes\A")));

    [Test]
    public void InstanceKey_DiffersBetweenFolders() =>
        Assert.That(AppHome.InstanceKey(@"D:\Homes\A"), Is.Not.EqualTo(AppHome.InstanceKey(@"D:\Homes\B")));
}
