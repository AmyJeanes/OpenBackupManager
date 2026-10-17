namespace OpenBackupManager.Platform.Windows.Tests;

// Without admin rights here, so this checks Explorer starts it, which the tray app needs when it has them
public class DesktopShellTests
{
    private string _folder = "";

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("obm-tests-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    // Through wscript, which opens no window, as a window could take the foreground from the UI tests running alongside
    [Test]
    public void TryStart_StartsTheProgram_WithItsArguments()
    {
        var script = Path.Combine(_folder, "write.js");
        File.WriteAllText(script, """new ActiveXObject("Scripting.FileSystemObject").CreateTextFile(WScript.Arguments(0)).Write("started");""");
        var file = Path.Combine(_folder, "started by explorer.txt");

        var started = DesktopShell.TryStart(Path.Combine(Environment.SystemDirectory, "wscript.exe"), $"//B //NoLogo \"{script}\" \"{file}\"");

        Assert.That(started, Is.True);
        Assert.That(() => File.Exists(file) ? File.ReadAllText(file) : null, Is.EqualTo("started").After(10).Seconds.PollEvery(100).MilliSeconds);
    }
}
