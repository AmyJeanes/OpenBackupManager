using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine.Tests;

public class BackendsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private AppPaths _home = null!;
    private IHost? _engine;

    [SetUp]
    public async Task SetUp()
    {
        _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-backends-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(_home.Config);
        await File.WriteAllTextAsync(_home.ConfigFile, "secret-store = \"file\"\n");
        _engine = EngineHost.Build(_home);
        await _engine.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_engine is not null)
        {
            await _engine.StopAsync();
            _engine.Dispose();
            _engine = null;
        }

        TestFolders.Delete(_home.State);
    }

    [Test]
    public async Task TheTestedOnes_ComeFirst_ThenTheRestByType()
    {
        using var client = await ConnectAsync();

        var backends = (await client.ListBackendsAsync(CancellationToken.None)).Backends;

        BackendInfo[] tested =
        [
            new("onedrive", "Microsoft OneDrive", Tested: true, 1, new Uri("https://rclone.org/onedrive/")),
            new("local", "Local Disk", Tested: true, 1, new Uri("https://rclone.org/local/")),
        ];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backends.Take(2), Is.EqualTo(tested));
            Assert.That(backends.Skip(2).Select(backend => backend.Type), Is.Ordered.Using<string>(StringComparer.Ordinal));
            Assert.That(backends.Skip(2).Any(backend => backend.Tested), Is.False);
            Assert.That(backends.Single(backend => backend.Type == "mega").Tier, Is.EqualTo(2));
            Assert.That(backends.Single(backend => backend.Type == "gcs").Docs, Is.EqualTo(new Uri("https://rclone.org/googlecloudstorage/")));
        }
    }

    // A wrapper, an old name, one rclone no longer maintains, and one its tests can't run on
    [TestCase("crypt")]
    [TestCase("tardigrade")]
    [TestCase("gphotos")]
    [TestCase("http")]
    public async Task OnesRcloneRatesOut_AreLeftOut(string type)
    {
        using var client = await ConnectAsync();

        var backends = (await client.ListBackendsAsync(CancellationToken.None)).Backends;

        Assert.That(backends.Select(backend => backend.Type), Has.No.Member(type));
    }

    [Test]
    public async Task TheRcloneAsked_IsntLeftRunning()
    {
        using var client = await ConnectAsync();

        await client.ListBackendsAsync(CancellationToken.None);

        Assert.That(File.Exists(RunFolder.Socket(_home, "backends.sock")), Is.False);
    }

    private async Task<EngineClient> ConnectAsync() =>
        await EngineClient.ConnectAsync(_home, Timeout, CancellationToken.None) ?? throw new AssertionException("The engine isn't running");
}
