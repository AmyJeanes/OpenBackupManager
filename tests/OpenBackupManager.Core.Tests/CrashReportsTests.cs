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
        _reports = new CrashReports(AppPaths.InHome(_home), _time, CrashReports.App);
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
            Assert.That(_reports.TakeNew(), Is.EqualTo(new CrashReport(CrashReports.App, newest)));
            Assert.That(_reports.TakeNew(), Is.Null);
        }

        _time.Advance(TimeSpan.FromSeconds(1));
        var next = File.ReadAllText(_reports.Write("Open Backup Manager", run, "Third"));
        Assert.That(_reports.TakeNew()?.Text, Is.EqualTo(next));
    }

    [Test]
    public void TakeNew_SaysWhichProgramsReportItIs()
    {
        var engine = Engine();
        engine.Start(300, RunStarted, "1.2.3");
        var report = File.ReadAllText(engine.Write("obmd", engine.Current!, "Something broke"));

        Assert.That(_reports.TakeNew(), Is.EqualTo(new CrashReport(CrashReports.Engine, report)));
    }

    [Test]
    public void TheEnginesReport_HasTheEndOfItsOwnLog()
    {
        var engine = Engine();
        engine.Start(300, RunStarted, "1.2.3");
        // The app's written to more recently
        Log("engine-20261014.log", DateTime.UtcNow.AddMinutes(-1), "The engine's log");
        Log("app-20261014.log", DateTime.UtcNow, "The app's log");

        var report = File.ReadAllText(engine.Write("obmd", engine.Current!, "Something broke"));

        Assert.That(report, Does.StartWith("obmd closed unexpectedly").And.Contain("The engine's log").And.Not.Contain("The app's log"));
    }

    [Test]
    public void TheAppAndTheEngine_KeepTheirOwnRunningFiles()
    {
        var engine = Engine();
        _reports.Start(100, RunStarted, "1.2.3");
        engine.Start(300, RunStarted, "1.2.3");
        _reports.Stopped();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_reports.Start(200, RunStarted, "1.2.3"), Is.Null);
            Assert.That(engine.Start(400, RunStarted, "1.2.3")?.ProcessId, Is.EqualTo(300));
        }
    }

    // Started just now, so nothing can be recorded about it. On macOS and Linux there's nothing to look in
    [Test]
    public void WriteRecorded_WritesNothing_WhenNothingWasRecorded()
    {
        var run = new Run(Environment.ProcessId, DateTimeOffset.Now, "1.2.3", null, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_reports.WriteRecorded("Open Backup Manager", run), Is.Null);
            Assert.That(Directory.Exists(Path.Combine(_home, "crashes")), Is.False);
        }
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
        // With the computer's boot ID on macOS and Linux, so a restart since can be told apart
        Assert.That(_reports.Start(200, RunStarted, "1.2.4"), Is.EqualTo(new Run(100, started, "1.2.3", "app-20261014.log", length, BootId.Current())));
    }

    [Test]
    public void Start_IgnoresARunningFileItDidntWrite()
    {
        File.WriteAllText(Path.Combine(_home, "app.running"), "not a process");

        Assert.That(_reports.Start(200, RunStarted, "1.2.3"), Is.Null);
    }

    [Test]
    public void Start_KeepsTheBootId()
    {
        File.WriteAllLines(Path.Combine(_home, "app.running"), ["100", RunStarted.ToString("O"), "1.2.3", "", "0", "0b1d6a3c-5c41-4b7e-9d0e-2a8f3c6e7d11"]);

        Assert.That(_reports.Start(200, RunStarted, "1.2.3")?.Boot, Is.EqualTo("0b1d6a3c-5c41-4b7e-9d0e-2a8f3c6e7d11"));
    }

    // As an older version wrote it, which can't be told from a restart
    [Test]
    public void Start_ReadsARunningFileWithNoBootId()
    {
        File.WriteAllLines(Path.Combine(_home, "app.running"), ["100", RunStarted.ToString("O"), "1.2.3", "", "0"]);

        Assert.That(_reports.Start(200, RunStarted, "1.2.3"), Is.EqualTo(new Run(100, RunStarted, "1.2.3", null, 0)));
    }

    [Test]
    public void RepeatOf_ARunThatClosedSoonAfterTheLastReport_IsThatReport()
    {
        var report = _reports.Write("Open Backup Manager", Start(), "Something broke");
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.That(_reports.RepeatOf(RunFrom(TimeSpan.FromSeconds(10))), Is.EqualTo(Path.GetFileName(report)));
    }

    // Also when the report came after it started, as the next start writes one about a run that left its running file
    [Test]
    public void RepeatOf_ARunThatReportedTheLastOne()
    {
        var run = RunFrom(TimeSpan.Zero);
        var report = _reports.WriteEnded("Open Backup Manager", RunFrom(TimeSpan.FromHours(1)), null, null);
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.That(_reports.RepeatOf(run), Is.EqualTo(Path.GetFileName(report)));
    }

    [Test]
    public void RepeatOf_NotARunThatLastedAMinute()
    {
        _reports.Write("Open Backup Manager", Start(), "Something broke");
        var run = RunFrom(TimeSpan.FromSeconds(10));
        _time.Advance(TimeSpan.FromSeconds(70));

        Assert.That(_reports.RepeatOf(run), Is.Null);
    }

    [Test]
    public void RepeatOf_NotWhenTheLastReportWasLongerAgo()
    {
        _reports.Write("Open Backup Manager", Start(), "Something broke");
        _time.Advance(TimeSpan.FromSeconds(90));
        var run = RunFrom(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.That(_reports.RepeatOf(run), Is.Null);
    }

    [Test]
    public void RepeatOf_NotAfterTheOtherProgramsReport()
    {
        var engine = Engine();
        engine.Start(300, RunStarted, "1.2.3");
        engine.Write("obmd", engine.Current!, "Something broke");
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.That(_reports.RepeatOf(RunFrom(TimeSpan.FromSeconds(10))), Is.Null);
    }

    [Test]
    public void RepeatOf_NotTheFirst() => Assert.That(_reports.RepeatOf(RunFrom(TimeSpan.Zero)), Is.Null);

    // On macOS and Linux, from the running file a run that crashed outside .NET, or was ended, left behind
    [Test]
    public void WriteEnded_WithNothingRecorded_SaysItDidntSayWhy()
    {
        var run = Start();
        Log("app-20261014.log", new DateTime(2026, 10, 14, 16, 29, 0, DateTimeKind.Utc), "The last thing it did");

        var report = File.ReadAllText(_reports.WriteEnded("obmd", run, null, null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report, Does.StartWith("obmd closed unexpectedly after 2026-10-14 16:29:00 +00:00, when it last wrote to its log"));
            Assert.That(report, Does.Contain("It didn't say why. It may have crashed, or been closed by the system or another program."));
            Assert.That(report, Does.Contain("Recent log:" + Environment.NewLine + "The last thing it did"));
        }
    }

    [Test]
    public void WriteEnded_WithNoLog_SaysWhenItStarted() =>
        Assert.That(
            File.ReadAllText(_reports.WriteEnded("obmd", Start(), null, null)),
            Does.StartWith("obmd closed unexpectedly some time after it started at 2026-10-14 16:00:00 +00:00"));

    [Test]
    public void WriteEnded_HasWhatMacOSRecordedAndWhatItPrinted()
    {
        var crashed = new DateTimeOffset(2026, 10, 14, 16, 12, 30, TimeSpan.Zero);

        var report = File.ReadAllText(_reports.WriteEnded(
            "obmd",
            Start(),
            new CrashEvent(crashed, "EXC_CRASH (SIGABRT), sent by obmd[100]"),
            "Process terminated. Something broke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report, Does.StartWith("obmd closed unexpectedly at 2026-10-14 16:12:30 +00:00"));
            Assert.That(report, Does.Contain(string.Join(
                Environment.NewLine,
                "macOS recorded this:",
                "EXC_CRASH (SIGABRT), sent by obmd[100]",
                "",
                "What it printed as it closed:",
                "Process terminated. Something broke")));
            Assert.That(report, Does.Not.Contain("It didn't say why"));
        }
    }

    // A FailFast's message and stack, after whatever it logged to the same place
    [Test]
    public void Ending_IsFromWhatNETPrintedAsItEnded()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CrashReports.Ending("12:00:01 [INF] started, process 100\nProcess terminated. Something broke\n   at Program.Main()\n"),
                Is.EqualTo("Process terminated. Something broke\n   at Program.Main()"));
            Assert.That(CrashReports.Ending("12:00:01 [INF] started, process 100\n"), Is.Null);
        }
    }

    private CrashReports Engine() => new(AppPaths.InHome(_home), _time, CrashReports.Engine);

    private Run Start()
    {
        _reports.Start(100, RunStarted, "1.2.3");
        return _reports.Current!;
    }

    // A run that started this long before now
    private Run RunFrom(TimeSpan ago) => new(100, _time.GetUtcNow() - ago, "1.2.3", null, 0);

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
