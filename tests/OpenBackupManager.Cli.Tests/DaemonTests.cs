using System.Diagnostics;

namespace OpenBackupManager.Cli.Tests;

public class DaemonTests
{
    private const string Started = "started, process";

    private readonly List<Obm> _running = [];
    private readonly List<string> _homes = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var obm in _running)
        {
            obm.Dispose();
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

        var (exitCode, output) = Obm.Run("daemon", "--home", home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("already running"));
            Assert.That(first.HasExited, Is.False);
        }
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
        var obm = Daemon(NewHome());
        obm.WaitFor(Started);

        using (var kill = Process.Start("kill", ["-" + signal, obm.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            kill.WaitForExit();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(obm.WaitForExit(), Is.Zero);
            Assert.That(obm.Output, Does.Contain("Engine stopped"));
        }
    }

    private Obm Daemon(string home)
    {
        var obm = Obm.Start("daemon", "--home", home);
        _running.Add(obm);
        return obm;
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

    private string NewHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _homes.Add(home);
        return home;
    }
}
