using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Engine.Tests;

// Against this OS, so each OS's default is checked by the CI runner for that OS
public class SecretsTests
{
    private string _home = "";
    private ConfigWatcher? _config;

    [SetUp]
    public void CreateHome() => _home = Directory.CreateTempSubdirectory("obm-secrets-").FullName;

    [TearDown]
    public void DeleteHome()
    {
        _config?.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    [Test]
    public void NothingChosen_IsThisOSsOwnStore() =>
        Assert.That(SecretsWith("").Chosen, Is.EqualTo(SecretStoreKinds.DefaultOn(CurrentOS.Platform)));

    [Test]
    public void TheChosenStore_IsUsed() =>
        Assert.That(SecretsWith("secret-store = \"file\"").Chosen, Is.EqualTo(SecretStoreKind.File));

    private Secrets SecretsWith(string text)
    {
        var path = Path.Combine(_home, "config.toml");
        File.WriteAllText(path, text);
        _config = new ConfigWatcher(new ConfigFile(path, _ => null), NullLogger<ConfigWatcher>.Instance);
        return new Secrets(new ServiceCollection().BuildServiceProvider(), _config, new FileStore(Path.Combine(_home, "secrets.json")));
    }
}
