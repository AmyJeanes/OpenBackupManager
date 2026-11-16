using FlaUI.Core.Tools;

namespace OpenBackupManager.App.Windows.UITests;

// The app run from a copy of itself without obmd.exe, as antivirus that quarantines it leaves an install
[Explicit("Takes over the desktop")]
[Category("Sandbox")]
public sealed class MissingEngineTests : AppTests
{
    private string _folder = "";

    protected override string AppPath => Path.Combine(_folder, Path.GetFileName(base.AppPath));

    [OneTimeSetUp]
    public void CopyTheAppWithoutTheEngine()
    {
        var built = Path.GetDirectoryName(base.AppPath)!;
        _folder = Directory.CreateTempSubdirectory("obm-no-engine-").FullName;
        foreach (var file in Directory.EnumerateFiles(built, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) != "obmd.exe")
            {
                var target = Path.Combine(_folder, Path.GetRelativePath(built, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }

    [OneTimeTearDown]
    public void DeleteTheCopy() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void SaysTheEngineCantStart_AndQuits()
    {
        Retry.WhileFalse(() => Text("StatusText") == "Can't reach the engine", Timeout, ignoreException: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Text("StatusText"), Is.EqualTo("Can't reach the engine"));
            Assert.That(Text("Detail"), Is.EqualTo("The app couldn't start its engine. Reinstalling the app should fix this."));
        }

        Quit();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(Home, "crash-*.txt", SearchOption.AllDirectories), Is.Empty, "Quitting shouldn't crash");
            Assert.That(Log(), Does.Contain("Couldn't start the engine, " + Path.Combine(_folder, "obmd.exe")));
        }
    }

    private string? Text(string id) => LaunchFlyout.FindFirstDescendant(c => c.ByAutomationId(id))?.Name;
}
