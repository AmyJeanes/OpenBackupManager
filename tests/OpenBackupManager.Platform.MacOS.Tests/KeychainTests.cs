using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using OpenBackupManager.Core;

namespace OpenBackupManager.Platform.MacOS.Tests;

// Each test makes its own keychain as the login keychain is made, so the real one is never touched
[Platform(Include = "MacOsX")]
[SupportedOSPlatform("macos")]
public class KeychainTests
{
    // errSecItemNotFound, as the tool's exit code
    private const int NotFound = 44;

    private string _file = "";
    private string _prefix = "";

    [SetUp]
    public void SetUp()
    {
        var id = Guid.NewGuid().ToString("N");
        _prefix = "OpenBackupManager-Tests-" + id;
        _file = Path.Combine(Path.GetTempPath(), $"obm-tests-{id[..8]}.keychain-db");
        Security("create-keychain", "-p", "test", _file);
    }

    [TearDown]
    public void TearDown() => Security("delete-keychain", _file);

    [TestCase("pässwörd 🔑")]
    [TestCase("two\nlines")]
    [TestCase("deadbeef")]
    [TestCase("with \"quotes\", a \\ and spaces")]
    public void Read_GivesWhatWasSaved(string secret)
    {
        var store = Store();

        store.Save("rclone-config", new Secret(secret));

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo(secret));
    }

    [Test]
    public void Save_PutsItInTheKeychain_AsBase64()
    {
        Store().Save("rclone-config", new Secret("pässwörd 🔑"));

        Assert.That(InTheKeychain("rclone-config"), Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes("pässwörd 🔑"))));
    }

    [Test]
    public void Read_AnItemTheToolSaved()
    {
        Security("add-generic-password", "-s", $"{_prefix}/rclone-config", "-a", "rclone-config", "-w", Convert.ToBase64String("by the tool"u8), _file);

        Assert.That(Store().Read("rclone-config")?.Reveal(), Is.EqualTo("by the tool"));
    }

    [Test]
    public void Read_IsNullWhenNothingWasSaved() => Assert.That(Store().Read("rclone-config"), Is.Null);

    [Test]
    public void Save_ReplacesTheSecret()
    {
        var store = Store();

        store.Save("rclone-config", new Secret("first"));
        store.Save("rclone-config", new Secret("second"));

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo("second"));
    }

    [Test]
    public void Save_TakesTheLongestSecretTheToolTakes()
    {
        var store = Store();
        var longest = new string('x', Enumerable.Range(1, Keychain.MaxCommand).Last(length => Fits(store, new string('x', length))));

        store.Save("rclone-config", new Secret(longest));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo(longest));
            Assert.That(() => store.Save("rclone-config", new Secret(longest + "x")), Throws.ArgumentException);
        }
    }

    [Test]
    public void Save_TakesANameWithQuotesAndBackslashes()
    {
        var store = Store();

        store.Save("a \"name\" \\ like this", new Secret("secret"));

        Assert.That(store.Read("a \"name\" \\ like this")?.Reveal(), Is.EqualTo("secret"));
    }

    [Test]
    public void Remove_RemovesTheSecret()
    {
        var store = Store();
        store.Save("rclone-config", new Secret("secret"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Remove("rclone-config"), Is.True);
            Assert.That(store.Read("rclone-config"), Is.Null);
            Assert.That(store.Remove("rclone-config"), Is.False);
        }
    }

    [Test]
    public void Remove_TakesItOutOfTheKeychain()
    {
        var store = Store();
        store.Save("rclone-config", new Secret("secret"));

        store.Remove("rclone-config");

        Assert.That(InTheKeychain("rclone-config"), Is.Null);
    }

    [Test]
    public void Stores_WithDifferentPrefixesKeepTheirOwnSecrets()
    {
        var store = Store();
        var other = new Keychain(_prefix + "-other", _file);

        store.Save("rclone-config", new Secret("this home's"));
        other.Save("rclone-config", new Secret("another home's"));

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo("this home's"));
    }

    private Keychain Store() => new(_prefix, _file);

    private static bool Fits(Keychain store, string secret) =>
        Encoding.UTF8.GetByteCount(store.SaveCommand("rclone-config", new Secret(secret))) <= Keychain.MaxCommand;

    // Read with the tool itself, so a mistake the store makes both saving and reading still shows
    private string? InTheKeychain(string name)
    {
        var (exitCode, output) = Run("find-generic-password", "-s", $"{_prefix}/{name}", "-a", name, "-w", _file);
        Assert.That(exitCode, Is.Zero.Or.EqualTo(NotFound), "security find-generic-password");
        return exitCode == 0 ? output.TrimEnd('\n') : null;
    }

    private static void Security(params string[] arguments) =>
        Assert.That(Run(arguments).ExitCode, Is.Zero, $"security {arguments[0]}");

    private static (int ExitCode, string Output) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("/usr/bin/security") { RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
