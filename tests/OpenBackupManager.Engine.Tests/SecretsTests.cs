using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine.Tests;

// Against this OS, so each OS's default is checked by the CI runner for that OS. Its own store is a fake
public class SecretsTests
{
    // The first is the OS's own store, then the file
    private static readonly SecretStoreKind OSStore = SecretStoreKinds.Parse(SecretStoreKinds.NamesOn(CurrentOS.Platform).First())!.Value;

    private string _home = "";
    private ConfigWatcher? _config;
    private FileStore _file = null!;
    private FakeStore _os = null!;

    [SetUp]
    public void CreateHome()
    {
        _home = Directory.CreateTempSubdirectory("obm-secrets-").FullName;
        _file = new FileStore(Path.Combine(_home, "secrets.json"));
        _os = new FakeStore();
    }

    [TearDown]
    public void DeleteHome()
    {
        _config?.Dispose();
        _file.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    [Test]
    public void NothingChosen_IsThisOSsOwnStore() =>
        Assert.That(SecretsWith("").Chosen, Is.EqualTo(SecretStoreKinds.DefaultOn(CurrentOS.Platform)));

    [Test]
    public void TheChosenStore_IsUsed() =>
        Assert.That(SecretsWith("secret-store = \"file\"").Chosen, Is.EqualTo(SecretStoreKind.File));

    [Test]
    public async Task Migrate_MovesEverySecret_ThenUsesTheNewStore()
    {
        var secrets = SecretsWith($"secret-store = \"{OSStore.Name()}\"");
        _os.Secrets["rclone-config"] = "one";
        _os.Secrets["heartbeat"] = "two";

        var result = await secrets.MigrateAsync("file", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(new MigrateSecretsResult(OSStore.Name(), 2, [], _file.Path)).Using<MigrateSecretsResult>(Same));
            Assert.That(await InTheFile(), Is.EquivalentTo(new Dictionary<string, string> { ["rclone-config"] = "one", ["heartbeat"] = "two" }));
            Assert.That(_os.Secrets, Is.Empty);
            Assert.That(secrets.Chosen, Is.EqualTo(SecretStoreKind.File));
            Assert.That(File.ReadAllText(ConfigPath), Does.Contain("secret-store = \"file\""));
        }
    }

    [Test]
    public async Task Migrate_KeepsWhatTheNewStoreAlreadyHas_ButTakesTheOldOnesValues()
    {
        var secrets = SecretsWith($"secret-store = \"{OSStore.Name()}\"");
        _os.Secrets["rclone-config"] = "in use";
        await _file.SaveAsync("rclone-config", new Secret("left from before"));
        await _file.SaveAsync("heartbeat", new Secret("only here"));

        await secrets.MigrateAsync("file", CancellationToken.None);

        Assert.That(await InTheFile(), Is.EquivalentTo(new Dictionary<string, string> { ["rclone-config"] = "in use", ["heartbeat"] = "only here" }));
    }

    [Test]
    public async Task AFailedCopy_LeavesBothStoresAndTheSettingAsTheyWere()
    {
        var secrets = SecretsWith("secret-store = \"file\"");
        await _file.SaveAsync("a-first", new Secret("one"));
        await _file.SaveAsync("b-second", new Secret("two"));
        await _file.SaveAsync("c-third", new Secret("three"));
        _os.Secrets["b-second"] = "left from before";
        _os.FailSaving = "c-third";

        Assert.That(() => secrets.MigrateAsync(OSStore.Name(), CancellationToken.None),
            Throws.InstanceOf<EngineException>().With.Message.EqualTo($"The secrets are still in {_file.Path}, as moving them failed: couldn't save c-third"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Secrets, Is.EquivalentTo(new Dictionary<string, string> { ["b-second"] = "left from before" }));
            Assert.That(await InTheFile(), Has.Count.EqualTo(3));
            Assert.That(secrets.Chosen, Is.EqualTo(SecretStoreKind.File));
        }
    }

    [Test]
    public void AStoreThatCantBeListed_LeavesEverythingAsItWas()
    {
        var secrets = SecretsWith($"secret-store = \"{OSStore.Name()}\"");
        _os.Secrets["rclone-config"] = "one";
        _os.FailListing = true;

        Assert.That(() => secrets.MigrateAsync("file", CancellationToken.None), Throws.InstanceOf<EngineException>().With.Message.EqualTo(
            $"The secrets are still in {OSStore.Title()}, as moving them failed: couldn't list them"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Secrets, Has.Count.EqualTo(1));
            Assert.That(secrets.Chosen, Is.EqualTo(OSStore));
        }
    }

    [Test]
    public async Task ACopyThatReadsBackDifferently_IsPutBack()
    {
        var secrets = SecretsWith("secret-store = \"file\"");
        await _file.SaveAsync("rclone-config", new Secret("one"));
        _os.GivesBackSomethingElse = true;

        Assert.That(() => secrets.MigrateAsync(OSStore.Name(), CancellationToken.None),
            Throws.InstanceOf<EngineException>().With.Message.EndsWith($"{OSStore.Title()} gave back something different for rclone-config"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Secrets, Is.Empty);
            Assert.That(secrets.Chosen, Is.EqualTo(SecretStoreKind.File));
        }
    }

    [Test]
    public async Task ACopyThatCantBePutBack_IsNamed()
    {
        var secrets = SecretsWith("secret-store = \"file\"");
        await _file.SaveAsync("a-first", new Secret("one"));
        await _file.SaveAsync("b-second", new Secret("two"));
        _os.FailSaving = "b-second";
        _os.FailRemoving = "a-first";

        Assert.That(() => secrets.MigrateAsync(OSStore.Name(), CancellationToken.None),
            Throws.InstanceOf<EngineException>().With.Message.EndsWith($". {OSStore.Title()} may still have copies of a-first"));
    }

    [Test]
    public async Task ASecretThatCantBeRemovedFromTheOldStore_IsNamed()
    {
        var secrets = SecretsWith($"secret-store = \"{OSStore.Name()}\"");
        _os.Secrets["rclone-config"] = "one";
        _os.Secrets["heartbeat"] = "two";
        _os.FailRemoving = "heartbeat";

        var result = await secrets.MigrateAsync("file", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Moved, Is.EqualTo(2));
            Assert.That(result.Left, Is.EqualTo((string[])["heartbeat"]));
            Assert.That(secrets.Chosen, Is.EqualTo(SecretStoreKind.File));
        }
    }

    [Test]
    public async Task ToTheStoreInUse_DoesNothing()
    {
        var secrets = SecretsWith("secret-store = \"file\"");
        await _file.SaveAsync("rclone-config", new Secret("one"));

        var result = await secrets.MigrateAsync("file", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(new MigrateSecretsResult("file", 0, [], _file.Path)).Using<MigrateSecretsResult>(Same));
            Assert.That(await InTheFile(), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task WhileTheEnvVarChoosesTheStore_ItsRefused_SayingWhatToDo()
    {
        var secrets = SecretsWith("", envVar: "file");
        await _file.SaveAsync("rclone-config", new Secret("one"));

        Assert.That(() => secrets.MigrateAsync(OSStore.Name(), CancellationToken.None), Throws.InstanceOf<EngineException>().With.Message.EqualTo(
            "OBM_SECRET_STORE is set where the engine runs, and it wins over config.toml, so the secrets can't be moved by changing secret-store there. "
            + "Set secret-store in config.toml to file, remove OBM_SECRET_STORE and restart the engine, then run this again"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Secrets, Is.Empty);
            Assert.That(File.ReadAllText(ConfigPath), Is.Empty);
        }
    }

    [Test]
    public void AStoreThisOSDoesntHave_IsRefused()
    {
        var elsewhere = Enum.GetValues<SecretStoreKind>().Select(kind => kind.Name()).First(name => !SecretStoreKinds.NamesOn(CurrentOS.Platform).Contains(name));

        Assert.That(() => SecretsWith("").MigrateAsync(elsewhere, CancellationToken.None),
            Throws.InstanceOf<EngineException>().With.Message.EqualTo($"This OS has no store called {elsewhere}. Choose {OSStore.Name()} or file"));
    }

    [Test]
    public async Task NothingChosen_OnlyChoosesTheStore()
    {
        Assume.That(SecretStoreKinds.DefaultOn(CurrentOS.Platform), Is.Null, "Only Linux has no store until one is chosen");
        var secrets = SecretsWith("");

        var result = await secrets.MigrateAsync("file", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.From, Is.Null);
            Assert.That(secrets.Chosen, Is.EqualTo(SecretStoreKind.File));
        }
    }

    [Test]
    public async Task ASaveWhileMoving_WaitsAndGoesToTheNewStore()
    {
        var secrets = SecretsWith($"secret-store = \"{OSStore.Name()}\"");
        _os.Secrets["rclone-config"] = "one";
        _os.Listing = new TaskCompletionSource();

        var migrating = secrets.MigrateAsync("file", CancellationToken.None);
        var saving = secrets.SaveAsync("heartbeat", new Secret("saved while moving"));
        _os.Listing.SetResult();
        await Task.WhenAll(migrating, saving);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await InTheFile(), Is.EquivalentTo(new Dictionary<string, string> { ["rclone-config"] = "one", ["heartbeat"] = "saved while moving" }));
            Assert.That(_os.Secrets, Is.Empty);
        }
    }

    [Test]
    public async Task ReadSaveAndRemove_UseTheChosenStore()
    {
        var secrets = SecretsWith($"secret-store = \"{OSStore.Name()}\"");

        await secrets.SaveAsync("rclone-config", new Secret("one"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Secrets, Is.EquivalentTo(new Dictionary<string, string> { ["rclone-config"] = "one" }));
            Assert.That((await secrets.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("one"));
            Assert.That(await secrets.RemoveAsync("rclone-config"), Is.True);
            Assert.That(_os.Secrets, Is.Empty);
        }
    }

    private string ConfigPath => Path.Combine(_home, "config.toml");

    private static bool Same(MigrateSecretsResult a, MigrateSecretsResult b) =>
        a.From == b.From && a.Moved == b.Moved && a.Left.SequenceEqual(b.Left) && a.File == b.File;

    private Secrets SecretsWith(string text, string? envVar = null)
    {
        File.WriteAllText(ConfigPath, text);
        _config = new ConfigWatcher(new ConfigFile(ConfigPath, name => name == "OBM_SECRET_STORE" ? envVar : null), NullLogger<ConfigWatcher>.Instance);
        var services = new ServiceCollection()
            .AddKeyedSingleton<ISecretStore>(OSStore, _os)
            .AddKeyedSingleton<ISecretStore>(SecretStoreKind.File, _file)
            .BuildServiceProvider();
        return new Secrets(services, _config, _file);
    }

    private async Task<Dictionary<string, string>> InTheFile()
    {
        var secrets = new Dictionary<string, string>();
        foreach (var name in await _file.ListAsync())
        {
            secrets[name] = (await _file.ReadAsync(name))!.Reveal();
        }

        return secrets;
    }

    // Fails or misbehaves as it's told, so each way a migrate can go wrong can be tried
    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public string? FailSaving { get; set; }

        public string? FailRemoving { get; set; }

        public bool GivesBackSomethingElse { get; set; }

        public bool FailListing { get; set; }

        // Listing waits for it once it has the names, so something else can be tried part way through
        public TaskCompletionSource? Listing { get; set; }

        public Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Secrets.TryGetValue(name, out var value) ? new Secret(GivesBackSomethingElse ? value + " changed" : value) : null);

        public Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default)
        {
            if (name == FailSaving)
            {
                throw new InvalidOperationException($"couldn't save {name}");
            }

            Secrets[name] = secret.Reveal();
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) =>
            name == FailRemoving ? throw new InvalidOperationException($"couldn't remove {name}") : Task.FromResult(Secrets.Remove(name));

        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (FailListing)
            {
                throw new InvalidOperationException("couldn't list them");
            }

            string[] names = [.. Secrets.Keys.Order(StringComparer.Ordinal)];
            if (Listing is not null)
            {
                await Listing.Task;
            }

            return names;
        }
    }
}
