using System.Diagnostics;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
#if WINDOWS
using OpenBackupManager.Platform.Windows;
#elif MACOS
using OpenBackupManager.Platform.MacOS;
#elif LINUX
using System.Runtime.Versioning;
using OpenBackupManager.Platform.Linux;
using Tmds.DBus.Protocol;
#endif

namespace OpenBackupManager.Cli.Tests;

// obm data remove taking a secret from the OS's own store, through obmd, for a home of its own. On Linux, with a D-Bus
// bus and GNOME Keyring of its own, as the Secret Service tests have, so the real keyring is never touched
[Explicit("Uses the OS's own store for secrets")]
[Category("Sandbox")]
public class DataSecretsTests
{
    private string _home = null!;
    private readonly Dictionary<string, string> _environment = [];
    private readonly List<Process> _started = [];

    [SetUp]
    public async Task SetUp()
    {
        _home = Directory.CreateTempSubdirectory("obm-tests-").FullName;
        _environment.Clear();
        _started.Clear();
#if LINUX
        if (OperatingSystem.IsLinux())
        {
            await StartKeyringAsync();
        }
#else
        await Task.CompletedTask;
#endif
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var process in _started)
        {
            process.Kill();
            process.WaitForExit();
            process.Dispose();
        }

        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Test]
    public async Task Remove_TakesTheSecretsFromTheOSsStore()
    {
        var (store, title) = Store();
        await store.SaveAsync("token", new Secret("not really a token"));

        var (exitCode, output) = Tool.Run("obm", _environment, "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain($"Removed 1 secret from {title}"));
            Assert.That(await store.ListAsync(), Is.Empty);
        }
    }

    // As obmd makes it for the home, so it has the same key
    private (ISecretStore Store, string Title) Store()
    {
        var key = AppHome.InstanceKey(AppPaths.InHome(_home).State);
#if WINDOWS
        return (new CredentialManager(key), SecretStoreKind.CredentialManager.Title());
#elif MACOS
        return OperatingSystem.IsMacOS() ? (new Keychain(key, Keychain.Login), SecretStoreKind.Keychain.Title()) : throw new PlatformNotSupportedException();
#elif LINUX
        return OperatingSystem.IsLinux()
            ? (new SecretService(key, _environment["DBUS_SESSION_BUS_ADDRESS"]), SecretStoreKind.SecretService.Title())
            : throw new PlatformNotSupportedException();
#else
        throw new PlatformNotSupportedException();
#endif
    }

#if LINUX
    // Unlocked with a password, as signing in does. obm passes the bus on to obmd
    [SupportedOSPlatform("linux")]
    private async Task StartKeyringAsync()
    {
        var folder = Path.Combine(_home, "keyring");
        Directory.CreateDirectory(Path.Combine(folder, "run"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var config = Path.Combine(folder, "bus.conf");
        File.WriteAllText(config, $"""
            <busconfig>
              <type>session</type>
              <listen>unix:path={Path.Combine(folder, "bus")}</listen>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true"/>
                <allow eavesdrop="true"/>
                <allow own="*"/>
              </policy>
            </busconfig>
            """);
        var bus = Start(folder, "dbus-daemon", $"--config-file={config}", "--nofork", "--print-address=1");
        _environment["DBUS_SESSION_BUS_ADDRESS"] = bus.StandardOutput.ReadLine()!;

        var keyring = Start(folder, "gnome-keyring-daemon", "--unlock", "--components=secrets", "--foreground");
        keyring.StandardInput.Write("test");
        keyring.StandardInput.Close();

        // With a timeout, as a bus that won't answer, such as one whose policy refuses its replies, would wait forever
        using var connection = new DBusConnection(_environment["DBUS_SESSION_BUS_ADDRESS"]);
        await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var watcher = await connection.WatchNameOwnerAsync("org.freedesktop.secrets");
        await watcher.WaitForOwnerAsync(timeout.Token);
    }

    // The keyring keeps its files and sockets in these, and GNOME_KEYRING_CONTROL would point it at the real one
    private Process Start(string folder, string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program, arguments) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (_environment.TryGetValue("DBUS_SESSION_BUS_ADDRESS", out var address))
        {
            start.Environment["DBUS_SESSION_BUS_ADDRESS"] = address;
        }

        start.Environment["XDG_RUNTIME_DIR"] = Path.Combine(folder, "run");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(folder, "data");
        start.Environment["HOME"] = folder;
        start.Environment.Remove("GNOME_KEYRING_CONTROL");
        var process = Process.Start(start)!;
        _started.Add(process);
        return process;
    }
#endif
}
