using System.Diagnostics;
using System.Runtime.Versioning;
using OpenBackupManager.Core;
using Tmds.DBus.Protocol;

namespace OpenBackupManager.Platform.Linux.Tests;

// Each test starts its own D-Bus bus and GNOME Keyring, unlocked with a password as signing in does, in folders of its
// own, so the real keyring is never touched
[Platform(Include = "Linux")]
[SupportedOSPlatform("linux")]
public class SecretServiceTests
{
    private const string Login = "/org/freedesktop/secrets/collection/login";

    private string _folder = "";
    private string _prefix = "";
    private Process? _bus;
    private string _address = "";
    private Process? _keyring;

    [SetUp]
    public async Task SetUp()
    {
        _bus = null;
        _keyring = null;
        _address = "";
        _prefix = "OpenBackupManager-Tests-" + Guid.NewGuid().ToString("N");
        _folder = Directory.CreateTempSubdirectory("obm-tests-").FullName;
        Directory.CreateDirectory(Path.Combine(_folder, "run"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        (_bus, _address) = StartBus("bus");

        var keyring = Start("gnome-keyring-daemon", "--unlock", "--components=secrets", "--foreground");
        keyring.RedirectStandardInput = true;
        _keyring = Process.Start(keyring)!;
        _keyring.StandardInput.Write("test");
        _keyring.StandardInput.Close();

        using var connection = new DBusConnection(_address);
        await connection.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var watcher = await connection.WatchNameOwnerAsync("org.freedesktop.secrets");
        await watcher.WaitForOwnerAsync(timeout.Token);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var process in new[] { _keyring, _bus })
        {
            process?.Kill();
            process?.WaitForExit();
            process?.Dispose();
        }

        Directory.Delete(_folder, recursive: true);
    }

    [TestCase("pässwörd 🔑")]
    [TestCase("two\nlines")]
    [TestCase("with \"quotes\", a \\ and spaces")]
    public async Task Read_GivesWhatWasSaved(string secret)
    {
        var store = Store();

        await store.SaveAsync("rclone-config", new Secret(secret));

        Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo(secret));
    }

    [Test]
    public async Task Save_PutsItInTheLoginKeyring()
    {
        await Store().SaveAsync("rclone-config", new Secret("pässwörd 🔑"));

        Assert.That(InTheKeyring("rclone-config"), Is.EqualTo(($"{_prefix}/rclone-config", "pässwörd 🔑")));
    }

    [Test]
    public async Task Read_AnItemSecretToolSaved()
    {
        Tool("secret-tool", "by the tool", "store", "--label=by the tool", "service", $"{_prefix}/rclone-config", "account", "rclone-config");

        Assert.That((await Store().ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("by the tool"));
    }

    [Test]
    public async Task Read_IsNullWhenNothingWasSaved() => Assert.That(await Store().ReadAsync("rclone-config"), Is.Null);

    [Test]
    public async Task Save_ReplacesTheSecret()
    {
        var store = Store();

        await store.SaveAsync("rclone-config", new Secret("first"));
        await store.SaveAsync("rclone-config", new Secret("second"));

        Assert.That(InTheKeyring("rclone-config"), Is.EqualTo(($"{_prefix}/rclone-config", "second")));
    }

    [Test]
    public async Task Save_LeavesNoCopyInAnotherKeyring()
    {
        SaveInTheSessionKeyring("rclone-config", "old");

        await Store().SaveAsync("rclone-config", new Secret("new"));

        Assert.That(InTheKeyring("rclone-config"), Is.EqualTo(($"{_prefix}/rclone-config", "new")));
    }

    [Test]
    public async Task Remove_RemovesTheSecret()
    {
        var store = Store();
        await store.SaveAsync("rclone-config", new Secret("secret"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await store.RemoveAsync("rclone-config"), Is.True);
            Assert.That(await store.ReadAsync("rclone-config"), Is.Null);
            Assert.That(await store.RemoveAsync("rclone-config"), Is.False);
        }
    }

    [Test]
    public async Task Remove_TakesItOutOfEveryKeyring()
    {
        var store = Store();
        await store.SaveAsync("rclone-config", new Secret("secret"));
        SaveInTheSessionKeyring("rclone-config", "another copy");

        await store.RemoveAsync("rclone-config");

        Assert.That(InTheKeyring("rclone-config"), Is.Null);
    }

    [Test]
    public async Task Stores_WithDifferentPrefixesKeepTheirOwnSecrets()
    {
        var store = Store();
        var other = new SecretService(_prefix + "-other", _address);

        await store.SaveAsync("rclone-config", new Secret("this home's"));
        await other.SaveAsync("rclone-config", new Secret("another home's"));

        Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("this home's"));
    }

    [Test]
    public async Task ALockedKeyring_IsntTakenForAMissingSecret()
    {
        var store = Store();
        await store.SaveAsync("rclone-config", new Secret("secret"));

        Tool("busctl", null, $"--address={_address}", "call", "org.freedesktop.secrets", "/org/freedesktop/secrets",
            "org.freedesktop.Secret.Service", "Lock", "ao", "1", Login);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => store.ReadAsync("rclone-config"), Throws.InvalidOperationException.With.Message.EqualTo("Secret Service couldn't read rclone-config: the keyring is locked"));
            Assert.That(() => store.SaveAsync("rclone-config", new Secret("new")), Throws.InvalidOperationException.With.Message.EndsWith("the keyring is locked"));
            Assert.That(() => store.RemoveAsync("rclone-config"), Throws.InvalidOperationException.With.Message.EndsWith("the keyring is locked"));
        }
    }

    [Test]
    public void ABusWithNoKeyring_SaysSo()
    {
        var (bus, address) = StartBus("empty-bus");
        try
        {
            Assert.That(() => new SecretService(_prefix, address).ReadAsync("rclone-config"),
                Throws.InvalidOperationException.With.Message.EqualTo("Secret Service couldn't read rclone-config: nothing provides it, such as GNOME Keyring or KDE Wallet"));
        }
        finally
        {
            bus.Kill();
            bus.WaitForExit();
            bus.Dispose();
        }
    }

    [Test]
    public void ABusThatIsntThere_SaysSo()
    {
        var address = $"unix:path={Path.Combine(_folder, "none")}";

        Assert.That(() => new SecretService(_prefix, address).ReadAsync("rclone-config"),
            Throws.InvalidOperationException.With.Message.StartsWith($"Secret Service couldn't read rclone-config: D-Bus at {address} can't be reached. "));
    }

    private SecretService Store() => new(_prefix, _address);

    // GNOME Keyring's session keyring, which it keeps in memory alongside the login one
    private void SaveInTheSessionKeyring(string name, string secret) =>
        Tool("secret-tool", secret, "store", "--collection=/org/freedesktop/secrets/collection/session", "--label=another copy",
            "service", $"{_prefix}/{name}", "account", name);

    // Found with secret-tool in every keyring, so a mistake the store makes both saving and reading still shows
    private (string Label, string Secret)? InTheKeyring(string name)
    {
        var output = Tool("secret-tool", null, "search", "--all", "service", $"{_prefix}/{name}", "account", name);
        if (output.Length == 0)
        {
            return null;
        }

        // Each item's path in brackets, then name = value lines
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines.Count(line => line.StartsWith('[')), Is.EqualTo(1), output);
        string Value(string key) => lines.Single(line => line.StartsWith(key + " = ", StringComparison.Ordinal))[(key.Length + 3)..];
        return (Value("label"), Value("secret"));
    }

    // Only what's put on it, without starting anything else when a name is called, as a session bus would
    private (Process Bus, string Address) StartBus(string socket)
    {
        var config = Path.Combine(_folder, socket + ".conf");
        File.WriteAllText(config, $"""
            <busconfig>
              <type>session</type>
              <listen>unix:path={Path.Combine(_folder, socket)}</listen>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true"/>
                <allow eavesdrop="true"/>
                <allow own="*"/>
              </policy>
            </busconfig>
            """);
        var bus = Process.Start(Start("dbus-daemon", $"--config-file={config}", "--nofork", "--print-address=1"))!;

        // Without one, the store and tools would fall back to the real session bus
        var address = bus.StandardOutput.ReadLine();
        Assert.That(address, Is.Not.Null.And.Not.Empty, "dbus-daemon");
        return (bus, address!);
    }

    private string Tool(string program, string? input, params string[] arguments)
    {
        var start = Start(program, arguments);
        start.RedirectStandardInput = true;
        using var process = Process.Start(start)!;
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.Zero, $"{program} {arguments[0]}: {error.Result}");
        return output;
    }

    private ProcessStartInfo Start(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, RedirectStandardError = true };

        // The keyring keeps its files and sockets in these, and GNOME_KEYRING_CONTROL would point it at the real one
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] = _address;
        start.Environment["XDG_RUNTIME_DIR"] = Path.Combine(_folder, "run");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(_folder, "data");
        start.Environment["HOME"] = _folder;
        start.Environment.Remove("GNOME_KEYRING_CONTROL");
        return start;
    }
}
