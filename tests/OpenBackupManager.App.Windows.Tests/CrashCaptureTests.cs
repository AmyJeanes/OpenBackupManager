using Microsoft.Extensions.Logging.Abstractions;
using OpenBackupManager.Core;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows.Tests;

public class CrashCaptureTests
{
    private string _home = "";

    [SetUp]
    public void CreateHome() => _home = Directory.CreateTempSubdirectory("obm-tests-").FullName;

    [TearDown]
    public void DeleteHome() => Directory.Delete(_home, recursive: true);

    // As when the disk is full. Only finding a crash .NET doesn't see needs the running file, so the app still starts
    [Test]
    public void Start_WhenTheRunningFileCantBeWritten_LetsTheAppStart()
    {
        // A folder in its place, which can't be written as a file
        Directory.CreateDirectory(Path.Combine(_home, "app.running"));
        var capture = new CrashCapture(
            new CrashReports(AppPaths.InHome(_home), TimeProvider.System, CrashReports.App),
            new AppInfo("Open Backup Manager", "1.2.3", null, new Uri("https://example.com")),
            NullLogger<CrashCapture>.Instance);

        Assert.That(capture.Start, Throws.Nothing);
    }
}
