using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli.Tests;

public class CrashesTests
{
    private string _home = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void SetUp()
    {
        _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 12, 3, TimeSpan.Zero));
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Test]
    public void List_NumbersThemNewestFirst()
    {
        Write(CrashReports.App, "Open Backup Manager", "First");
        Write(CrashReports.Engine, "obmd", "Second");

        var (exitCode, output) = Tool.Run("obm", "crashes", "list", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Is.EqualTo(Lines(
                "1. (new) obmd closed unexpectedly at 2026-11-04 09:12:04 +00:00",
                "   Second",
                "2. (new) Open Backup Manager closed unexpectedly at 2026-11-04 09:12:03 +00:00",
                "   First",
                "",
                "See one with obm crashes show <number>")));
        }
    }

    [Test]
    public void List_SaysWhenThereArentAny() =>
        Assert.That(Tool.Run("obm", "crashes", "list", "--home", _home), Is.EqualTo((0, "There aren't any crash reports")));

    [Test]
    public void List_Json()
    {
        var file = Write(CrashReports.Engine, "obmd", "Something broke");

        using var obm = Tool.Start("obm", "crashes", "list", "--json", "--home", _home);
        Assert.That(obm.WaitForExit(), Is.Zero, obm.Output);
        using var reports = JsonDocument.Parse(obm.Stdout);
        var report = reports.RootElement.EnumerateArray().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.GetProperty("number").GetInt32(), Is.EqualTo(1));
            Assert.That(report.GetProperty("program").GetString(), Is.EqualTo("engine"));
            Assert.That(report.GetProperty("file").GetString(), Is.EqualTo(file));
            Assert.That(report.GetProperty("summary").GetString(), Is.EqualTo("Something broke"));
            Assert.That(report.GetProperty("new").GetBoolean(), Is.True);
        }
    }

    [Test]
    public void Show_PrintsTheNewest_AndCountsItSeen()
    {
        Write(CrashReports.App, "Open Backup Manager", "First");
        var newest = Write(CrashReports.Engine, "obmd", "Second");

        var (exitCode, output) = Tool.Run("obm", "crashes", "show", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Is.EqualTo(Lines(newest, "", File.ReadAllText(newest).TrimEnd())));
            Assert.That(Tool.Run("obm", "crashes", "list", "--home", _home).Output, Does.Not.Contain("(new)"));
        }
    }

    [Test]
    public void Show_AnOlderOne_LeavesNewerOnesNew()
    {
        var older = Write(CrashReports.App, "Open Backup Manager", "First");
        Write(CrashReports.Engine, "obmd", "Second");

        var (exitCode, output) = Tool.Run("obm", "crashes", "show", "2", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.StartWith(older));
            Assert.That(Tool.Run("obm", "crashes", "list", "--home", _home).Output, Does.Contain("1. (new) obmd").And.Contain("2. Open Backup Manager"));
        }
    }

    [Test]
    public void Show_SaysWhenThereIsntOne()
    {
        Assert.That(Tool.Run("obm", "crashes", "show", "--home", _home), Is.EqualTo((1, "obm: there aren't any crash reports")));

        Write(CrashReports.App, "Open Backup Manager", "First");
        Assert.That(
            Tool.Run("obm", "crashes", "show", "2", "--home", _home),
            Is.EqualTo((1, "obm: there's no crash report 2. See them with obm crashes list")));
    }

    [Test]
    public void Report_PrintsWhatItShares_AndTheLink()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Write(CrashReports.Engine, "obmd", "Can't open " + Path.Combine(home, "notes.txt"));

        var (exitCode, output) = Tool.Run("obm", "crashes", "report", "--no-browser", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.StartWith("obmd closed unexpectedly at 2026-11-04 09:12:03 +00:00"));
            Assert.That(output, Does.Contain("Can't open " + Path.Combine("~", "notes.txt")).And.Not.Contain(home));
            Assert.That(output, Does.Contain(Lines(
                "Sensitive details have been redacted where possible, but check the report before you submit. GitHub issues are public.",
                "Open this to start a GitHub issue with it. Nothing is sent until you submit it there:",
                "https://github.com/AmyJeanes/OpenBackupManager/issues/new?template=crash_report.md&title=obmd%20closed%20unexpectedly%3A%20Can%27t%20open%20~")));
            Assert.That(Tool.Run("obm", "crashes", "list", "--home", _home).Output, Does.Not.Contain("(new)"));
        }
    }

    // Where a browser would open on the computer's own screen, or as a text browser in the terminal
    [Test]
    public void Report_OverSSH_PrintsTheLink()
    {
        Write(CrashReports.Engine, "obmd", "Something broke");

        var (exitCode, output) = Tool.Run(
            "obm",
            new Dictionary<string, string> { ["SSH_CONNECTION"] = "192.0.2.1 50000 192.0.2.2 22" },
            "crashes",
            "report",
            "--home",
            _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("Open this to start a GitHub issue").And.Not.Contain("Opening"));
        }
    }

    [Test]
    public void Report_TooLongForTheLink_HasTheWholeReportInAFile()
    {
        var file = Write(CrashReports.Engine, "obmd", string.Join(Environment.NewLine, Enumerable.Range(1, 500).Select(i => $"   at Frame{i}()")));
        var whole = Path.Combine(Path.GetTempPath(), Path.GetFileName(file));

        try
        {
            var (exitCode, output) = Tool.Run("obm", "crashes", "report", "-n", "--home", _home);
            var link = output.Split(Environment.NewLine)[^1];
            var body = Uri.UnescapeDataString(link.Split("&body=")[^1]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain($"It's too long to fill in, so it's saved in {whole}. Paste it under Logs in the issue."));
                Assert.That(File.ReadAllText(whole), Does.Contain("   at Frame500()"));
                Assert.That(link, Does.StartWith("https://github.com/"));
                Assert.That(BugReport.Length(link), Is.AtMost(BugReport.LongestLink));
                Assert.That(body, Does.Contain("```text\n⚠️ PASTE THE CRASH REPORT HERE. It was too long to fill in, so it's saved in ").And.Contain($"{Path.GetFileName(file)}.\n```"));
                Assert.That(body, Does.Not.Contain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            }
        }
        finally
        {
            File.Delete(whole);
        }
    }

    [Test]
    public void Report_SaysWhenThereIsntOne() =>
        Assert.That(Tool.Run("obm", "crashes", "report", "--no-browser", "--home", _home), Is.EqualTo((1, "obm: there aren't any crash reports")));

    // The engine may not be running because it crashed. A script running obm status doesn't use them up
    [Test]
    public void Status_ListsNewReports_WithoutCountingThemSeen()
    {
        Write(CrashReports.App, "Open Backup Manager", "First");
        Write(CrashReports.Engine, "obmd", "Second");

        using var obm = Tool.Start("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(obm.WaitForExit(), Is.EqualTo(1));
            Assert.That(obm.Stderr, Does.Contain("the engine isn't running"));
            Assert.That(obm.Stdout, Is.EqualTo(Lines(
                "",
                "New crash reports:",
                "  1. obmd closed unexpectedly at 2026-11-04 09:12:04 +00:00",
                "  2. Open Backup Manager closed unexpectedly at 2026-11-04 09:12:03 +00:00",
                "See one with obm crashes show <number>")));
            Assert.That(Tool.Run("obm", "crashes", "list", "--home", _home).Output, Does.Contain("1. (new) obmd").And.Contain("2. (new) Open Backup Manager"));
        }
    }

    [Test]
    public void Status_WithTheEngineRunning_ListsThemAfterIt()
    {
        Write(CrashReports.Engine, "obmd", "Something broke");
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.StartWith("The engine is running"));
            Assert.That(output, Does.EndWith(Lines(
                "",
                "New crash reports:",
                "  1. obmd closed unexpectedly at 2026-11-04 09:12:03 +00:00",
                "See it with obm crashes show")));
        }
    }

    [Test]
    public void Status_LeavesThemOutOfItsJson()
    {
        Write(CrashReports.Engine, "obmd", "Something broke");

        using var obm = Tool.Start("obm", "status", "--json", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(obm.WaitForExit(), Is.EqualTo(1));
            Assert.That(obm.Stdout, Is.Empty);
        }
    }

    [Test]
    public void Status_SaysNothing_OnceTheyreSeen()
    {
        Write(CrashReports.Engine, "obmd", "Something broke");
        Tool.Run("obm", "crashes", "show", "--home", _home);

        Assert.That(Tool.Run("obm", "status", "--home", _home).Output, Does.Not.Contain("crash report"));
    }

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    // As the app or the engine writes it, a second after the last
    private string Write(string name, string program, string details)
    {
        var file = new CrashReports(AppPaths.InHome(_home), _time, name).Write(program, new Run(100, _time.GetUtcNow(), "1.2.3", null, 0), details);
        _time.Advance(TimeSpan.FromSeconds(1));
        return file;
    }
}
