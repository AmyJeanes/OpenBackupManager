using System.Net.Sockets;
using System.Runtime.Versioning;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Cli.Tests;

// obm data remove, always with a home of its own, so it never touches the real data
public class DataTests
{
    private string _home = null!;

    [SetUp]
    public void SetUp()
    {
        _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_home);
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
    public async Task Remove_StopsTheEngine_AndRemovesWhatItMade_ButNothingElse()
    {
        using (var store = new FileStore(Path.Combine(_home, "secrets.json")))
        {
            await store.SaveAsync("token", new Secret("not really a token"));
        }

        File.WriteAllText(Path.Combine(_home, "config.toml"), "secret-store = \"file\"\n");
        File.WriteAllText(Path.Combine(_home, "notes.txt"), "Someone's own file");
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain($"What Open Backup Manager keeps in {_home}"));
            Assert.That(output, Does.Contain("The engine has stopped"));
            Assert.That(output, Does.Contain("Removed all of Open Backup Manager's data"));
            Assert.That(daemon.HasExited, Is.True);
            Assert.That(Directory.GetFileSystemEntries(_home).Select(Path.GetFileName), Is.EqualTo((string[])["notes.txt"]));
        }
    }

    // A home is a folder someone chose, so the folders made in it may hold their files too
    [Test]
    public void Remove_LeavesSomeonesOwnFiles_InTheFoldersItMakes()
    {
        string[] made = ["logs/engine-20261107.log", "logs/app-20261107.log", "logs/obmd-stderr.log", "crashes/crash-20261107-101500-000-engine.txt", "crashes/shown", "run/engine.sock"];
        string[] theirs = ["logs/build.log", "crashes/core.txt", "run/start.sh", "cache/big.dat"];
        foreach (var file in made.Concat(theirs))
        {
            var path = Path.Combine(_home, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (file == "run/engine.sock" && !OperatingSystem.IsWindows())
            {
                // As an engine that's gone leaves it. On macOS, a plain file there reads as a socket obm can't open
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Bind(new UnixDomainSocketEndPoint(path));
            }
            else
            {
                File.WriteAllText(path, "");
            }
        }

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(
                Directory.GetFiles(_home, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(_home, file).Replace(Path.DirectorySeparatorChar, '/')),
                Is.EquivalentTo(theirs));
        }
    }

    // It goes with the rest, rather than being read
    [Test]
    public void Remove_WithSecretsJsonTooBrokenToRead_StillRemovesEverything()
    {
        File.WriteAllText(Path.Combine(_home, "secrets.json"), "not JSON");

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(_home, Does.Not.Exist);
        }
    }

    // As on a server, where nothing could have been saved there, so there's nothing to warn about
    [Test]
    [Platform("Linux")]
    public void Remove_WithNoSecretService_SaysNothingAboutIt()
    {
        var (exitCode, output) = Tool.Run("obm", new Dictionary<string, string> { ["DBUS_SESSION_BUS_ADDRESS"] = "" }, "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Not.Contain("Secret Service"));
        }
    }

    [Test]
    public void Remove_WhenThatEmptiesTheHome_RemovesItToo()
    {
        Directory.CreateDirectory(Path.Combine(_home, "logs"));
        File.WriteAllText(Path.Combine(_home, "logs", "engine-20261107.log"), "Started");
        File.WriteAllText(Path.Combine(_home, "config.toml"), "");

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Not.Contain("The engine has stopped"));
            Assert.That(_home, Does.Not.Exist);
        }
    }

    [Test]
    public void Remove_WithoutATerminal_AsksForYes_AndRemovesNothing()
    {
        File.WriteAllText(Path.Combine(_home, "config.toml"), "");

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.Contain("obm: --yes is required when not running interactively"));
            Assert.That(Path.Combine(_home, "config.toml"), Does.Exist);
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void Remove_InDocker_SaysToRemoveTheVolume()
    {
        var programs = Path.Combine(_home, "programs");
        Tool.CopyTo(programs);
        File.WriteAllText(Path.Combine(programs, "installed-by"), "docker\n");

        var (exitCode, output) = Tool.Run(Path.Combine(programs, "obm"), "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo("obm: in Docker, remove the container and its /config volume instead, such as with docker compose down -v"));
        }
    }

    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void Remove_WhileTheAppRuns_SaysToQuitIt()
    {
        using var app = ProcessLock.TryTake("app", AppPaths.InHome(_home));

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo("obm: the app is running, so quit it from its tray icon first"));
        }
    }
}
