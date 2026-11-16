namespace OpenBackupManager.Cli.Tests;

// obm service, where nothing is installed. ServiceTests install for real
public class ServiceCommandTests
{
    [TestCase("install")]
    [TestCase("uninstall")]
    [Platform("Win")]
    public void OnWindows_SaysTheAppStartsTheEngine(string action)
    {
        var (exitCode, output) = Tool.Run("obm", "service", action);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("the app starts the engine").And.Contain("Task Scheduler"));
        }
    }

    [Test]
    [Platform("Win")]
    public void OnWindows_IsntListed() => Assert.That(Tool.Run("obm", "--help").Output, Does.Not.Contain("service"));

    [Test]
    [Platform(Exclude = "Win")]
    public void IsListed() => Assert.That(Tool.Run("obm", "--help").Output, Does.Contain("service"));

    [Test]
    [Platform(Exclude = "Win")]
    public void Uninstall_WhenItIsnt_SaysSo()
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);

        var (exitCode, output) = Tool.Run("obm", "service", "uninstall", "--home", home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output, Does.Contain("isn't installed as a service"));
        }
    }

    // As in most containers, or under WSL with systemd off, where systemctl is there but can't reach systemd
    [Test]
    [Platform("Linux")]
    public void Install_WithoutSystemd_LeavesAnEngineStartedByHandRunning()
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var byHand = Tool.Start("obmd", "--home", home);
            byHand.WaitFor("started, process");
            var withoutSystemd = new Dictionary<string, string> { ["XDG_RUNTIME_DIR"] = Path.Combine(home, "nowhere"), ["DBUS_SESSION_BUS_ADDRESS"] = "" };

            var (exitCode, output) = Tool.Run("obm", withoutSystemd, "service", "install", "--home", home);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(output, Does.Contain("systemd isn't present or can't be reached"));
                Assert.That(output, Does.Not.Contain("Stopping the engine"));
                Assert.That(byHand.HasExited, Is.False, "The engine was stopped");
            }
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
