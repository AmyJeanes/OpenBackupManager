using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

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

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void AHomeItCantOpen_SaysSo()
    {
        Assume.That(Environment.UserName, Is.Not.EqualTo("root"), "root can open it anyway");
        Directory.CreateDirectory(_home);
        File.SetUnixFileMode(_home, UnixFileMode.None);
        try
        {
            var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(output, Does.Contain("Can't open " + Path.Combine(_home, "run", "engine.sock")));
                Assert.That(output, Does.Not.Contain("Unhandled exception"));
            }
        }
        finally
        {
            File.SetUnixFileMode(_home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void AHomeThatsAFile_SaysSo()
    {
        File.WriteAllText(_home, "");
        try
        {
            var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(output, Does.Contain("Can't open " + Path.Combine(_home, "run", "engine.sock")));
                Assert.That(output, Does.Not.Contain("Unhandled exception"));
            }
        }
        finally
        {
            File.Delete(_home);
        }
    }

    [Test]
    public void SaysWhereSecretsAreKept()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_home, "config.toml"), "secret-store = \"file\"\n");
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");

        Assert.That(
            Tool.Run("obm", "status", "--home", _home).Output,
            Does.Contain($"Secrets: in {Path.Combine(_home, "secrets.json")}, protected only by its permissions"));
    }

    [Test]
    public void ShowsWhatsWrongWithConfig_WhileItsStillRunning()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");
        File.WriteAllText(Path.Combine(_home, "config.toml"), "colour = \"blue\"\n[updates]\nprereleases = \"yes\"\n");
        daemon.WaitFor("so the last good settings stay in use");

        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain(string.Join(
                Environment.NewLine,
                "  config.toml has an error, so its latest changes aren't applied:",
                "    Error on line 3: updates.prereleases should be true or false",
                "    Warning on line 1: colour isn't a known setting")));
        }
    }

    [Test]
    public void ConfigWithNoProblems_ShowsNothing() =>
        Assert.That(Status.ConfigProblems([], CultureInfo.InvariantCulture), Is.Empty);

    [Test]
    public void ConfigWithOnlyWarnings_SaysItsStillUsed() =>
        Assert.That(
            Status.ConfigProblems([new ConfigProblem(2, ConfigProblemKind.UnknownSection, IsError: false, "colours")], CultureInfo.InvariantCulture),
            Is.EqualTo(["config.toml has a warning, but its settings are applied:", "  Warning on line 2: [colours] isn't a known section"]));

    [Test]
    public void ConfigWithSeveral_SaysSo()
    {
        var two = Status.ConfigProblems(
            [new ConfigProblem(1, ConfigProblemKind.VersionTooLow, IsError: true), new ConfigProblem(3, ConfigProblemKind.RemoteNeedsId, IsError: true)],
            CultureInfo.InvariantCulture);

        Assert.That(two.First(), Is.EqualTo("config.toml has errors, so its latest changes aren't applied:"));
    }

    [TestCase("credential-manager", null, "in Credential Manager")]
    [TestCase("keychain", null, "in the login Keychain")]
    [TestCase(null, null, "no store chosen yet. Choose one with secret-store in config.toml or OBM_SECRET_STORE: secret-service or file")]
    public void Secrets(string? store, string? file, string expected) =>
        Assert.That(Status.Secrets(new SecretsStatus(store, file), OSPlatform.Linux), Is.EqualTo(expected));

    [TestCase(0, "0s")]
    [TestCase(65, "1m 5s")]
    [TestCase(3600, "1h 0m 0s")]
    [TestCase(90061, "1d 1h 1m 1s")]
    public void Uptime(int seconds, string expected) =>
        Assert.That(Status.Uptime(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
}
