using Microsoft.Extensions.Time.Testing;

namespace OpenBackupManager.Core.Tests;

public class CrashReportsTests
{
    private static readonly DateTimeOffset RunStarted = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);

    private string _home = "";
    private string _logs = "";
    private FakeTimeProvider _time = null!;
    private CrashReports _reports = null!;

    [SetUp]
    public void SetUp()
    {
        _home = Directory.CreateTempSubdirectory("obm-crashes-").FullName;
        _logs = Path.Combine(_home, "logs");
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 16, 30, 0, TimeSpan.Zero));
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _reports = new CrashReports(AppPaths.InHome(_home), _time, "app-*.log");
    }

    [TearDown]
    public void DeleteHome() => Directory.Delete(_home, recursive: true);

    [Test]
    public void Write_HasTheErrorVersionOSAndTheEndOfTheRunsLog()
    {
        Log("app-20261013.log", DateTime.UtcNow.AddDays(-1), "Yesterday");
        Log("app-20261014.log", DateTime.UtcNow.AddMinutes(-1), "An earlier run");
        var run = Start();
        Log("app-20261014.log", DateTime.UtcNow, [.. Enumerable.Range(1, 150).Select(i => $"Line {i}"), "   at a stack trace's line"]);

        var file = _reports.Write("Open Backup Manager", run, Thrown().ToString());

        var report = File.ReadAllText(file);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Path.GetDirectoryName(file), Is.EqualTo(Path.Combine(_home, "crashes")));
            Assert.That(report, Does.StartWith("Open Backup Manager closed unexpectedly at 2026-10-14 16:30:00 +00:00"));
            Assert.That(report, Does.Contain("Version: 1.2.3"));
            Assert.That(report, Does.Contain("OS: "));
            Assert.That(report, Does.Contain("System.InvalidOperationException: Something broke").And.Contain(nameof(Thrown)));
            // The newest 100 lines, from the run alone
            Assert.That(report.Split(Environment.NewLine), Does.Contain("Line 52").And.Contain("   at a stack trace's line").And.No.EqualTo("Line 51"));
            Assert.That(report, Does.Not.Contain("An earlier run").And.Not.Contain("Yesterday"));
        }
    }

    [Test]
    public void Write_HasTheWholeNewestLog_WhenTheRunStartedAnother()
    {
        Log("app-20261014.log", DateTime.UtcNow.AddMinutes(-1), "An earlier run");
        var run = Start();
        Log("app-20261015.log", DateTime.UtcNow, "After midnight");

        var report = File.ReadAllText(_reports.Write("Open Backup Manager", run, "Something broke"));

        Assert.That(report, Does.Contain("Recent log:" + Environment.NewLine + "After midnight").And.Not.Contain("An earlier run"));
    }

    [Test]
    public void Write_WithNoLog_SaysSo() =>
        Assert.That(File.ReadAllText(_reports.Write("Open Backup Manager", Start(), "Something broke")), Does.Contain("Recent log:" + Environment.NewLine + "(none)"));

    [Test]
    public void Write_ClosedAt_SaysWhenItHappened()
    {
        var closed = new DateTimeOffset(2026, 10, 14, 9, 15, 0, TimeSpan.Zero);

        var file = _reports.Write("Open Backup Manager", Start(), "Windows recorded an error", closed);

        Assert.That(File.ReadAllText(file), Does.StartWith("Open Backup Manager closed unexpectedly at 2026-10-14 09:15:00 +00:00"));
    }

    [Test]
    public void Write_KeepsTheNewest10()
    {
        var run = Start();
        var files = Enumerable.Range(0, 12).Select(_ =>
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            return _reports.Write("Open Backup Manager", run, "Something broke");
        }).ToList();

        Assert.That(Directory.GetFiles(Path.Combine(_home, "crashes"), "crash-*.txt").Order(), Is.EqualTo(files[2..]));
    }

    [Test]
    public void TakeNew_GivesEachReportOnce()
    {
        var run = Start();
        Assert.That(_reports.TakeNew(), Is.Null);

        _reports.Write("Open Backup Manager", run, "First");
        _time.Advance(TimeSpan.FromSeconds(1));
        var newest = File.ReadAllText(_reports.Write("Open Backup Manager", run, "Second"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_reports.TakeNew(), Is.EqualTo(newest));
            Assert.That(_reports.TakeNew(), Is.Null);
        }

        _time.Advance(TimeSpan.FromSeconds(1));
        var next = File.ReadAllText(_reports.Write("Open Backup Manager", run, "Third"));
        Assert.That(_reports.TakeNew(), Is.EqualTo(next));
    }

    [Test]
    public void Start_GivesNothing_WhenTheLastRunQuit()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_reports.Start(100, RunStarted, "1.2.3"), Is.Null);
            _reports.Stopped();
            Assert.That(_reports.Start(200, RunStarted, "1.2.3"), Is.Null);
        }
    }

    [Test]
    public void Start_GivesTheLastRun_WhenItDidntQuit()
    {
        Log("app-20261014.log", DateTime.UtcNow, "An earlier run");
        var started = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(1));
        _reports.Start(100, started, "1.2.3");

        var length = new FileInfo(Path.Combine(_logs, "app-20261014.log")).Length;
        Assert.That(_reports.Start(200, RunStarted, "1.2.4"), Is.EqualTo(new Run(100, started, "1.2.3", "app-20261014.log", length)));
    }

    [Test]
    public void Start_IgnoresARunningFileItDidntWrite()
    {
        File.WriteAllText(Path.Combine(_home, "running"), "not a process");

        Assert.That(_reports.Start(200, RunStarted, "1.2.3"), Is.Null);
    }

    private Run Start()
    {
        _reports.Start(100, RunStarted, "1.2.3");
        return _reports.Current!;
    }

    // As the app writes its log, with the time it was last written to, which picks the newest file
    private void Log(string file, DateTime written, params string[] lines)
    {
        var path = Path.Combine(Directory.CreateDirectory(_logs).FullName, file);
        File.AppendAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, written);
    }

    // With a stack trace, as a real crash has
    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("Something broke");
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }
}
