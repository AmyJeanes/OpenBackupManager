using OpenBackupManager.Core;

namespace OpenBackupManager.ViewModels.Tests;

[SetUICulture("en-GB")]
public class CrashViewModelTests
{
    private static readonly AppInfo App = new("Open Backup Manager", "1.2.3", null, new Uri("https://example.com/obm"));

    [Test]
    public void ShowsTheReport()
    {
        var crash = new CrashViewModel(App, new CrashReport(CrashReports.App, "Open Backup Manager closed unexpectedly at …"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crash.Title, Is.EqualTo("Open Backup Manager closed unexpectedly"));
            Assert.That(crash.Report, Is.EqualTo("Open Backup Manager closed unexpectedly at …"));
        }
    }

    [Test]
    public void TheEnginesReport_SaysItWasTheEngine() =>
        Assert.That(new CrashViewModel(App, new CrashReport(CrashReports.Engine, "obmd closed unexpectedly at …")).Title, Is.EqualTo("Open Backup Manager's engine stopped unexpectedly"));
}
