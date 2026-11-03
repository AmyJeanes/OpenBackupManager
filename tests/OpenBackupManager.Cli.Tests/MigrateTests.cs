using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

// Only what leaves the OS's own store alone, which the engine's tests try with a fake
public class MigrateTests
{
    private const string File = "/home/amy/.local/state/openbackupmanager/secrets.json";

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
    public void ToTheStoreInUse_SaysSo()
    {
        Directory.CreateDirectory(_home);
        System.IO.File.WriteAllText(Path.Combine(_home, "config.toml"), "secret-store = \"file\"\n");
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "secrets", "migrate", "file", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("Secrets are already kept in " + Path.Combine(_home, "secrets.json")));
        }
    }

    [Test]
    public void NoEngine_ExitsWithAnError()
    {
        var (exitCode, output) = Tool.Run("obm", "secrets", "migrate", "file", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("the engine isn't running"));
        }
    }

    [Test]
    public void AStoreThisOSDoesntHave_IsRefused()
    {
        var elsewhere = Enum.GetValues<SecretStoreKind>().Select(kind => kind.Name()).First(name => !SecretStoreKinds.NamesOn(CurrentOS.Platform).Contains(name));

        var (exitCode, output) = Tool.Run("obm", "secrets", "migrate", elsewhere, "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(output, Does.Contain(elsewhere));
        }
    }

    [TestCase(null, 0, "secret-service", "Secrets will be kept in Secret Service")]
    [TestCase("file", 0, "file", "Secrets are already kept in " + File)]
    [TestCase("secret-service", 0, "file", "There were no secrets to move. They'll be kept in " + File)]
    [TestCase("secret-service", 1, "file", "Moved 1 secret from Secret Service to " + File)]
    [TestCase("file", 3, "secret-service", "Moved 3 secrets from " + File + " to Secret Service")]
    public void Moved(string? from, int moved, string store, string expected) =>
        Assert.That(Migrate.Moved(new MigrateSecretsResult(moved, [], File, from), store), Is.EqualTo(expected));
}
