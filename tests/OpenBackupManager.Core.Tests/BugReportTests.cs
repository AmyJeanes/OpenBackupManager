using System.Text.RegularExpressions;

namespace OpenBackupManager.Core.Tests;

public class BugReportTests
{
    [Test]
    public void Link_FillsInTheTemplate()
    {
        var report = Report(["Something broke", "   at Program.Main()"], ["Started"]);

        var link = BugReport.Link(report, "The logs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(link, Does.StartWith(
                "https://github.com/AmyJeanes/OpenBackupManager/issues/new?template=crash_report.md"
                + "&title=obmd%20closed%20unexpectedly%3A%20Something%20broke&body="));
            Assert.That(Body(link), Is.EqualTo("""
                ### What happened

                <!-- What were you doing when it closed? -->

                ### Steps to reproduce

                <!-- How to make it happen again, if you know -->

                ### What you expected

                ### Open Backup Manager version

                1.2.3+abc

                ### Operating system

                Ubuntu 24.04.1 LTS (X64)

                ### Remote

                <!-- e.g. OneDrive personal -->

                ### Logs

                <!-- Sensitive details have been redacted where possible, but check the report before you submit. GitHub issues are public. -->

                <details>
                <summary>Crash report</summary>

                ```text
                The logs
                ```

                </details>

                ### AI assistance (if any)

                <!-- Which tool, and what it did -->
                """.ReplaceLineEndings("\n")));
        }
    }

    [Test]
    public void Link_FencesTheLogsPastAnyBackticks() =>
        Assert.That(Body(BugReport.Link(Report(["Something broke"], []), "Failed to read ```json")), Does.Contain("````text\nFailed to read ```json\n````"));

    // So an issue reads the same however it was made
    [Test]
    public void HasTheBugReportFormsHeadings()
    {
        var form = Regex.Matches(File.ReadAllText(Template("bug_report.yml")), @"^\s+label: (.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim());
        var template = Regex.Matches(File.ReadAllText(Template("crash_report.md")), @"^### (.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value.Trim());
        var link = Regex.Matches(Body(BugReport.Link(Report(["Something broke"], []), "")), @"^### (.+)$", RegexOptions.Multiline).Select(match => match.Groups[1].Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(template, Is.EqualTo(form));
            Assert.That(link, Is.EqualTo(form));
        }
    }

    private static string Body(string link) => Uri.UnescapeDataString(link.Split("&body=")[1]);

    private static string Template(string name) => Path.Combine(AppContext.BaseDirectory, "IssueTemplates", name);

    private static string Report(string[] details, string[] log) => string.Join(
        "\n",
        ["obmd closed unexpectedly at 2026-11-05 00:13:54 +00:00", "", "Version: 1.2.3+abc", "OS: Ubuntu 24.04.1 LTS (X64)", ".NET: .NET 10.0.0", "", .. details, "", "Recent log:", .. log]);
}
