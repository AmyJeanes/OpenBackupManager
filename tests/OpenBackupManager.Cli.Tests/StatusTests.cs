using System.Globalization;
using System.Reflection;

namespace OpenBackupManager.Cli.Tests;

public class StatusTests
{
    private string _home = null!;

    [SetUp]
    public void SetUp() => _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Test]
    public void ShowsTheRunningEngine()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("The engine is running"));
            Assert.That(output, Does.Contain("Version: " + typeof(Status).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion));
            Assert.That(output, Does.Contain("Process: " + daemon.Id.ToString(CultureInfo.InvariantCulture)));
            Assert.That(output, Does.Contain("Started by: you or another program"));
            Assert.That(output, Does.Contain("Home: " + _home));
        }
    }

    [Test]
    public void SaysTheAppStartedIt()
    {
        using var daemon = Tool.Start("obmd", "--home", _home, "--started-by", "app");
        daemon.WaitFor("started, process");

        Assert.That(Tool.Run("obm", "status", "--home", _home).Output, Does.Contain("Started by: the app"));
    }

    [Test]
    public void NoEngine_ExitsWithAnError()
    {
        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("the engine isn't running"));
        }
    }

    [Test]
    public void OnceTheEngineDies_ItsNotRunning()
    {
        using (var daemon = Tool.Start("obmd", "--home", _home))
        {
            daemon.WaitFor("started, process");
            daemon.Kill();
        }

        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("the engine isn't running"));
        }
    }

    [TestCase(0, "0s")]
    [TestCase(65, "1m 5s")]
    [TestCase(3600, "1h 0m 0s")]
    [TestCase(90061, "1d 1h 1m 1s")]
    public void Uptime(int seconds, string expected) =>
        Assert.That(Status.Uptime(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
}
