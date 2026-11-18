using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine.Tests;

public class RemotesTests
{
    private AppPaths _home = null!;

    [SetUp]
    public void SetUp()
    {
        _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-remotes-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(_home.Config);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_home.State, recursive: true);

    [Test]
    public async Task EachRemote_HasItsOwnRclone_UntilTheEngineStops()
    {
        await File.WriteAllTextAsync(_home.ConfigFile, "[[remotes]]\nid = \"onedrive\"\n\n[[remotes]]\nid = \"nas\"\n");
        using var engine = EngineHost.Build(_home);
        await engine.StartAsync();
        var remotes = engine.Services.GetServices<IHostedService>().OfType<Remotes>().Single();
        string[] sockets = [remotes.Options("onedrive").Socket, remotes.Options("nas").Socket];

        var pids = new List<int>();
        foreach (var socket in sockets)
        {
            using var client = new RcClient(socket);
            pids.Add((await client.PidAsync(CancellationToken.None)).Pid);
        }

        await engine.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pids, Is.Unique);
            // Each quit rather than was killed, as that leaves its socket
            Assert.That(sockets.Where(File.Exists), Is.Empty);
        }
    }

    [Test]
    public void Options_NameItsFilesFromItsId()
    {
        using var engine = EngineHost.Build(_home);
        var remotes = engine.Services.GetServices<IHostedService>().OfType<Remotes>().Single();

        var options = remotes.Options("onedrive-2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Rclone, Is.EqualTo(BundledRclone.FilePath));
            Assert.That(options.Config, Is.EqualTo(Path.Combine(_home.State, "rclone", "onedrive-2.conf")));
            Assert.That(options.CacheFolder, Is.EqualTo(Path.Combine(_home.Cache, "rclone", "onedrive-2")));
            Assert.That(options.LogFile, Is.EqualTo(Path.Combine(_home.Logs, "rclone-onedrive-2.log")));
            Assert.That(Path.GetFileName(options.Socket), Does.Match("^rc-[0-9a-f]{8}\\.sock$"));
            Assert.That(options.Socket, Is.Not.EqualTo(remotes.Options("onedrive").Socket));
            Assert.That(options.UserAgent, Is.EqualTo($"ISV|AmyJeanes|OpenBackupManager/{AppVersion.Current}"));
        }
    }
}
