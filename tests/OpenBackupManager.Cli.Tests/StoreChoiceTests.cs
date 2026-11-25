using System.Globalization;
using System.Runtime.InteropServices;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

// With what the engine does standing in for it, as only on Linux is there no store until one is chosen
public class StoreChoiceTests
{
    private const string File = "/home/amy/.local/state/openbackupmanager/secrets.json";

    private static readonly SecretsStatus NoneChosen = new();

    private readonly List<string> _chosen = [];
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [TearDown]
    public void TearDown()
    {
        _chosen.Clear();
        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _output.Dispose();
        _error.Dispose();
    }

    [Test]
    public async Task WithAStoreChosen_AsksNothing()
    {
        var chosen = await EnsureAsync(new SecretsStatus("secret-service"), "");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chosen, Is.True);
            Assert.That(_chosen, Is.Empty);
            Assert.That(_output.ToString() + _error, Is.Empty);
        }
    }

    [Test]
    public async Task Asks_WithEachStoresFacts()
    {
        var chosen = await EnsureAsync(NoneChosen, "2\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chosen, Is.True);
            Assert.That(_chosen, Is.EqualTo(["file"]));
            Assert.That(_output.ToString(), Does.Contain(string.Join(
                Environment.NewLine,
                "  1. secret-service: " + SecretStoreKind.SecretService.Facts(CultureInfo.CurrentCulture),
                "  2. file: " + SecretStoreKind.File.Facts(CultureInfo.CurrentCulture),
                "",
                "Which? (1 or 2): Secrets will be kept in " + File)));
            Assert.That(_error.ToString(), Is.Empty);
        }
    }

    [Test]
    public async Task ByName_ChoosesIt()
    {
        await EnsureAsync(NoneChosen, " secret-service \n");

        Assert.That(_chosen, Is.EqualTo(["secret-service"]));
    }

    [TestCase("")]
    [TestCase("3")]
    [TestCase("0")]
    [TestCase("keychain")]
    public async Task AnythingElse_AsksAgain_AsThereIsNoDefault(string answer)
    {
        var chosen = await EnsureAsync(NoneChosen, answer + "\n1\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chosen, Is.True);
            Assert.That(_chosen, Is.EqualTo(["secret-service"]));
            Assert.That(Times(_output.ToString(), "Which? (1 or 2): "), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task AStoreThatCantBeUsed_SaysWhy_AndAsksAgain()
    {
        var chosen = await EnsureAsync(NoneChosen, "1\n2\n", refuse: "secret-service");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chosen, Is.True);
            Assert.That(_chosen, Is.EqualTo(["secret-service", "file"]));
            Assert.That(_error.ToString(), Is.EqualTo("obm: Secret Service has no default keyring" + Environment.NewLine));
            Assert.That(_output.ToString(), Does.EndWith("Secrets will be kept in " + File + Environment.NewLine));
        }
    }

    [Test]
    public async Task WithNoTerminal_NamesTheSetting_WithoutAsking()
    {
        var chosen = await EnsureAsync(NoneChosen, input: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chosen, Is.False);
            Assert.That(_chosen, Is.Empty);
            Assert.That(_output.ToString(), Is.Empty);
            Assert.That(_error.ToString(), Is.EqualTo(NamesTheSetting));
        }
    }

    [Test]
    public async Task InputEndingWithNoAnswer_NamesTheSetting()
    {
        var chosen = await EnsureAsync(NoneChosen, "\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chosen, Is.False);
            Assert.That(_chosen, Is.Empty);
            Assert.That(_error.ToString(), Is.EqualTo(NamesTheSetting));
        }
    }

    private static string NamesTheSetting =>
        "obm: this device has nowhere to keep secrets yet. Choose where with obm secrets migrate secret-service or obm secrets migrate file, "
        + "or set secret-store in config.toml, then run this again" + Environment.NewLine;

    private static int Times(string text, string part) => text.Split(part).Length - 1;

    // On Linux, whichever OS the tests run on
    private async Task<bool> EnsureAsync(SecretsStatus secrets, string? input, string? refuse = null)
    {
        using var reader = input is null ? null : new StringReader(input);
        return await StoreChoice.EnsureAsync(
            secrets,
            OSPlatform.Linux,
            (store, _) =>
            {
                _chosen.Add(store);
                return store == refuse
                    ? throw new EngineException("Secret Service has no default keyring")
                    : Task.FromResult(new MigrateSecretsResult(0, [], File));
            },
            reader,
            _output,
            _error,
            CancellationToken.None);
    }
}
