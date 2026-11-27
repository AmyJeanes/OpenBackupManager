using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine.Tests;

public class UpdateWatcherTests
{
    private string _folder = "";
    private ConfigWatcher? _config;

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-watcher-").FullName;

    [TearDown]
    public void DeleteFolder()
    {
        _config?.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void Identity_IsNull_OnceObmdIsGone() =>
        Assert.That(UpdateWatcher.Identity(Path.Combine(_folder, "obmd")), Is.Null);

    // Or the engine would remove its service and stop, though obmd is still there
    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void Identity_CantBeTold_WhileObmdCantBeRead()
    {
        Assume.That(Environment.UserName, Is.Not.EqualTo("root"), "Root can read it anyway");
        var obmd = Path.Combine(_folder, "obmd");
        File.WriteAllText(obmd, "");
        File.SetUnixFileMode(_folder, UnixFileMode.None);

        Assert.That(() => UpdateWatcher.Identity(obmd), Throws.InstanceOf<UnauthorizedAccessException>());
    }

    // Such as one installed as the engine started, which only asking its version can tell
    [Test]
    public async Task AnotherVersion_ThereAsItStarts_IsNoticed()
    {
        var obmd = Obmd("1.0.0");
        var (watcher, time) = Watcher(obmd);
        using (watcher)
        {
            Install(obmd, "2.0.0");
            await watcher.StartAsync(CancellationToken.None);
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (watcher.Installed is null && DateTime.UtcNow < until)
            {
                time.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(100);
            }

            await watcher.StopAsync(CancellationToken.None);
        }

        Assert.That(watcher.Installed, Is.EqualTo("2.0.0"));
    }

    // Such as a package reinstalled, which has nothing new to restart into
    [Test]
    public async Task TheVersionRunning_Reinstalled_IsntReported()
    {
        var obmd = Obmd(AppVersion.Current);
        var asked = Path.Combine(_folder, "asked");
        var (watcher, time) = Watcher(obmd);
        using (watcher)
        {
            await watcher.StartAsync(CancellationToken.None);
            Install(obmd, AppVersion.Current, asked);
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (!File.Exists(asked) && DateTime.UtcNow < until)
            {
                time.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(100);
            }

            // Once it's done with what it asked
            await watcher.StopAsync(CancellationToken.None);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(asked), Is.True, "Its version wasn't asked");
            Assert.That(watcher.Installed, Is.Null);
        }
    }

    private (UpdateWatcher Watcher, FakeTimeProvider Time) Watcher(string obmd)
    {
        var time = new FakeTimeProvider();
        _config = new ConfigWatcher(new ConfigFile(Path.Combine(_folder, "config.toml")), NullLogger<ConfigWatcher>.Instance);
        var watcher = new UpdateWatcher(
            new EngineOptions(EngineStarter.Other, Obmd: obmd),
            _config,
            new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
            time,
            NullLogger<UpdateWatcher>.Instance);
        return (watcher, time);
    }

    // One that prints its version, as obmd --version does
    private string Obmd(string version)
    {
        var obmd = Path.Combine(_folder, OperatingSystem.IsWindows() ? "obmd.cmd" : "obmd");
        Write(obmd, version);
        return obmd;
    }

    // Replaced whole, as a package does, here with a time a minute on. Asked is a file it makes once it's run
    private static void Install(string obmd, string version, string? asked = null)
    {
        var replacement = obmd + ".new";
        Write(replacement, version, asked);
        File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(obmd).AddMinutes(1));
        File.Move(replacement, obmd, overwrite: true);
    }

    private static void Write(string file, string version, string? asked = null)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(file, (asked is null ? "" : $"@type nul > \"{asked}\"\r\n") + $"@echo {version}\r\n");
            return;
        }

        File.WriteAllText(file, "#!/bin/sh\n" + (asked is null ? "" : $"touch '{asked}'\n") + $"echo '{version}'\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
