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
        Assert.That(new CrashViewModel(App, new CrashReport(CrashReports.Engine, "obmd closed unexpectedly at …")).Title, Is.EqualTo("Open Backup Manager's engine closed unexpectedly"));

    [Test]
    public void ReportOnGitHub_OpensAnIssueWithTheReport_WithoutTheHomeFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var crash = new CrashViewModel(App, new CrashReport(CrashReports.Engine, Report("Can't open " + Path.Combine(home, "notes.txt"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crash.ToPaste, Is.Null);
            Assert.That(Uri.UnescapeDataString(crash.GitHubLink), Does.Contain("Can't open " + Path.Combine("~", "notes.txt")).And.Not.Contain(home));
        }
    }

    // As VS Code's issue reporter does
    [Test]
    public void ReportOnGitHub_TooLongForTheLink_PutsItOnTheClipboard()
    {
        var crash = new CrashViewModel(App, new CrashReport(CrashReports.Engine, Report([.. Enumerable.Range(1, 500).Select(i => $"   at Frame{i}()")])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crash.ToPaste, Does.Contain("   at Frame500()"));
            Assert.That(Uri.UnescapeDataString(crash.GitHubLink), Does.Contain("```text\n⚠️ PASTE THE CRASH REPORT HERE. It was too long to fill in, so it's on your clipboard.\n```"));
        }
    }

    private static string Report(params string[] details) => string.Join(
        Environment.NewLine,
        ["obmd closed unexpectedly at 2026-11-05 00:13:54 +00:00", "", "Version: 1.2.3", "OS: Windows", ".NET: .NET 10.0.0", "", .. details, "", "Recent log:", "(none)"]);
}
