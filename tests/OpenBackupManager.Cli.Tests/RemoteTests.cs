namespace OpenBackupManager.Cli.Tests;

public class RemoteTests
{
    private string _home = null!;

    [SetUp]
    public void SetUp()
    {
        _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_home);
        // With no rclone config, so it needs setting up
        File.WriteAllText(Path.Combine(_home, "config.toml"), "secret-store = \"file\"\n\n[[remotes]]\nid = \"onedrive\"\nroot = \"\"\n");
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
    public void Status_ShowsARemoteThatNeedsSettingUp()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("needs setting up");

        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain(string.Join(
                Environment.NewLine,
                "  Remotes:",
                "    onedrive: needs setting up, as it has no rclone config",
                "  Try one again now with obm remote retry <id>")));
        }
    }

    [Test]
    public void Retry_TriesItAgain()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("needs setting up");

        var (exitCode, output) = Tool.Run("obm", "remote", "retry", "onedrive", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("Trying onedrive again. obm status shows how it goes"));
            // Still with no config, so it says so again
            Assert.That(() => Times(daemon, "needs setting up"), Is.EqualTo(2).After(15_000, 100));
        }
    }

    private static int Times(Tool tool, string text) => tool.Output.Split(text).Length - 1;

    [Test]
    public void Retry_ARemoteThatIsntThere_SaysSo()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "remote", "retry", "nas", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("obm: There's no remote called nas"));
        }
    }

    [Test]
    public void Retry_WithNoEngine_SaysSo()
    {
        var (exitCode, output) = Tool.Run("obm", "remote", "retry", "onedrive", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("the engine isn't running. It tries every remote as it starts"));
        }
    }
}
