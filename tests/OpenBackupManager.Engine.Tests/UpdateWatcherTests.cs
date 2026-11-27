using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine.Tests;

public class UpdateWatcherTests
{
    private string _folder = "";

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-watcher-").FullName;

    [TearDown]
    public void DeleteFolder()
    {
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

    // As the engine says it's started once it's started its services, while ExecuteAsync runs later on the thread pool
    [Test]
    public async Task AnUpdate_InstalledAsItStarts_IsNoticed()
    {
        var obmd = Obmd("1.0.0");
        var time = new FakeTimeProvider();
        using var config = new ConfigWatcher(new ConfigFile(Path.Combine(_folder, "config.toml")), NullLogger<ConfigWatcher>.Instance);
        using var watcher = new UpdateWatcher(
            new EngineOptions(EngineStarter.Other, Obmd: obmd),
            config,
            new ApplicationLifetime(NullLogger<ApplicationLifetime>.Instance),
            time,
            NullLogger<UpdateWatcher>.Instance);

        Install(obmd, "2.0.0");
        await watcher.StartAsync(CancellationToken.None);
        // It's only taken once it's the same at two checks in a row
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (watcher.Installed is null && DateTime.UtcNow < until)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
        }

        await watcher.StopAsync(CancellationToken.None);
        Assert.That(watcher.Installed, Is.EqualTo("2.0.0"));
    }

    // One that prints its version, as obmd --version does
    private string Obmd(string version)
    {
        var obmd = Path.Combine(_folder, OperatingSystem.IsWindows() ? "obmd.cmd" : "obmd");
        Write(obmd, version);
        return obmd;
    }

    // Replaced whole, as a package does, here with a time a minute on
    private static void Install(string obmd, string version)
    {
        var replacement = obmd + ".new";
        Write(replacement, version);
        File.SetLastWriteTimeUtc(replacement, File.GetLastWriteTimeUtc(obmd).AddMinutes(1));
        File.Move(replacement, obmd, overwrite: true);
    }

    private static void Write(string file, string version)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(file, $"@echo {version}\r\n");
            return;
        }

        File.WriteAllText(file, $"#!/bin/sh\necho {version}\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
