namespace OpenBackupManager.ViewModels.Tests;

[SetUICulture("en-GB")]
public class CrashViewModelTests
{
    [Test]
    public void ShowsTheReport()
    {
        var crash = new CrashViewModel(new AppInfo("Open Backup Manager", "1.2.3", null, new Uri("https://example.com/obm")), "Open Backup Manager closed unexpectedly at …");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crash.Title, Is.EqualTo("Open Backup Manager closed unexpectedly"));
            Assert.That(crash.Report, Is.EqualTo("Open Backup Manager closed unexpectedly at …"));
        }
    }
}
