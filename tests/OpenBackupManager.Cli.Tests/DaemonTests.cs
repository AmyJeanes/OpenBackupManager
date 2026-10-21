using System.Diagnostics;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

public class DaemonTests
{
    private const string Started = "started, process";

    private readonly List<Tool> _running = [];
    private readonly List<string> _homes = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var daemon in _running)
        {
            daemon.Dispose();
        }

        foreach (var home in _homes)
        {
            Directory.Delete(home, recursive: true);
        }

        // NUnit uses one instance for every test in the class
        _running.Clear();
        _homes.Clear();
    }

    [Test]
    public void Runs_AndLogsToItsOwnFiles()
    {
        var home = NewHome();
        Daemon(home).WaitFor(Started);

        // The console gets each line just before the file does
        Assert.That(() => Log(home), Does.Contain(Started).After(30_000, 100));
        Assert.That(Directory.GetFiles(Path.Combine(home, "logs"), "engine-*.log"), Has.Length.EqualTo(1));
    }

    [Test]
    public void ASecondOne_ForTheSameHome_Exits()
    {
        var home = NewHome();
        var first = Daemon(home);
        first.WaitFor(Started);

        var (exitCode, output) = Tool.Run("obmd", "--home", home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("already running"));
            Assert.That(first.HasExited, Is.False);
        }
    }

    // Only the tray app and obm service install use it
    [TestCase("app")]
    [TestCase("service")]
    public void StartedBy_Runs_ButIsntListed(string startedBy)
    {
        Daemon(NewHome(), "--started-by", startedBy).WaitFor(Started);

        Assert.That(Tool.Run("obmd", "--help").Output, Does.Not.Contain("--started-by"));
    }

    [Test]
    public void AnotherHome_RunsAlongside()
    {
        Daemon(NewHome()).WaitFor(Started);

        Assert.That(() => Daemon(NewHome()).WaitFor(Started), Throws.Nothing);
    }

    [Test]
    public void OnceOneDies_AnotherCanStart()
    {
        var home = NewHome();
        var first = Daemon(home);
        first.WaitFor(Started);

        first.Kill();

        Assert.That(() => Daemon(home).WaitFor(Started), Throws.Nothing);
    }

    // As Ctrl+C and service managers ask it to. Windows can't send these to another process, so there it's checked by hand
    [TestCase("INT")]
    [TestCase("TERM")]
    [Platform(Exclude = "Win")]
    public void Stops_WhenAsked(string signal)
    {
        var daemon = Daemon(NewHome());
        daemon.WaitFor(Started);

        using (var kill = Process.Start("kill", ["-" + signal, daemon.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            kill.WaitForExit();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(daemon.WaitForExit(), Is.Zero);
            Assert.That(daemon.Output, Does.Contain("Engine stopped"));
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void AHomeTooLongForItsSocket_SaysWhy()
    {
        var home = NewHome(new string('x', 120));

        var (exitCode, output) = Tool.Run("obmd", "--home", home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("obmd: The engine's socket").And.Contain("--home"));
        }
    }

#if DEBUG
    [Test]
    [Explicit("Crashes obmd, which the system records")]
    [Category("Sandbox")]
    public async Task ACrash_IsReported()
    {
        var home = NewHome();
        var daemon = Daemon(home);
        daemon.WaitFor(Started);

        await CrashAsync(home, CrashParams.OnAnotherThread);
        var exitCode = daemon.WaitForExit();

        using (Assert.EnterMultipleScope())
        {
            // On Windows, .NET's exception code, or the fail-fast code NativeAOT ends with. SIGABRT elsewhere
            var crashed = !OperatingSystem.IsWindows() ? 134 : Tool.Published is null ? unchecked((int)0xE0434352) : unchecked((int)0xC0000409);
            Assert.That(exitCode, Is.EqualTo(crashed));
            Assert.That(
                Report(home),
                Does.StartWith("obmd closed unexpectedly")
                    .And.Contain("System.InvalidOperationException: Test crash on another thread")
                    .And.Contain(Started));
            Assert.That(File.Exists(Path.Combine(home, "engine.running")), Is.False);
        }
    }

    // On Windows, from what Windows recorded. macOS and Linux keep no such record, so there it's only logged
    [Test]
    [Explicit("Crashes obmd, which the system records")]
    [Category("Sandbox")]
    public async Task ACrashNETNeverSaw_IsFoundAtTheNextStart()
    {
        var home = NewHome();
        var crashed = Daemon(home);
        crashed.WaitFor(Started);
        await CrashAsync(home, CrashParams.FailFast);
        crashed.WaitForExit();

        var next = Daemon(home);
        next.WaitFor(Started);

        if (OperatingSystem.IsWindows())
        {
            Assert.That(Report(home), Does.StartWith("obmd closed unexpectedly").And.Contain("Windows recorded this").And.Contain("Test crash that fails fast"));
        }
        else
        {
            Assert.That(next.Output, Does.Contain("The last run ended without stopping"));
        }
    }
#endif

    [Test]
    public async Task Stopping_LeavesNothingToReport()
    {
        var home = NewHome();
        var stopped = Daemon(home);
        stopped.WaitFor(Started);
        using (var engine = await ConnectAsync(home))
        {
            await engine.StopAsync(CancellationToken.None);
        }

        stopped.WaitForExit();
        var next = Daemon(home);
        next.WaitFor(Started);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(next.Output, Does.Not.Contain("The last run"));
            Assert.That(Directory.Exists(Path.Combine(home, "crashes")), Is.False);
        }
    }

    private static async Task<EngineClient> ConnectAsync(string home) =>
        (await EngineClient.ConnectAsync(AppPaths.InHome(home), TimeSpan.FromSeconds(10), CancellationToken.None))!;

#if DEBUG
    private static async Task CrashAsync(string home, string how)
    {
        using var engine = await ConnectAsync(home);
        await engine.CrashAsync(how, CancellationToken.None);
    }
#endif

    private static string Report(string home) =>
        File.ReadAllText(Directory.GetFiles(Path.Combine(home, "crashes"), "crash-*-engine.txt").Single());

    private Tool Daemon(string home, params string[] options)
    {
        var daemon = Tool.Start("obmd", ["--home", home, .. options]);
        _running.Add(daemon);
        return daemon;
    }

    // Read while the engine still has it open
    private static string Log(string home)
    {
        var logs = Directory.Exists(Path.Combine(home, "logs")) ? Directory.GetFiles(Path.Combine(home, "logs"), "engine-*.log") : [];
        if (logs.Length == 0)
        {
            return "";
        }

        using var log = new StreamReader(new FileStream(logs[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return log.ReadToEnd();
    }

    private string NewHome(string suffix = "")
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8] + suffix);
        _homes.Add(home);
        return home;
    }
}
