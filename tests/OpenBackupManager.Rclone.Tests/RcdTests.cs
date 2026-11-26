using System.Diagnostics;
using System.Net;

namespace OpenBackupManager.Rclone.Tests;

public class RcdTests
{
    private string _folder = null!;

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-rcd-").FullName;

    [TearDown]
    public void TearDown() => TestFolders.Delete(_folder);

    [Test]
    public async Task StartAsync_AnswersOnItsSocket_AndQuitsCleanly()
    {
        var options = Options();
        await using (var rcd = await Rcd.StartAsync(options, CancellationToken.None))
        {
            var version = await rcd.Client.VersionAsync(CancellationToken.None);

            Assert.That(version.Version, Is.EqualTo(BundledRclone.Version));
        }

        // A killed rcd leaves its socket behind
        Assert.That(File.Exists(options.Socket), Is.False);
    }

    [Test]
    public async Task Providers_HaveRclonesOwnRating()
    {
        await using var rcd = await Rcd.StartAsync(Options(), CancellationToken.None);

        var providers = (await rcd.Client.ProvidersAsync(CancellationToken.None)).Providers.ToDictionary(provider => provider.Prefix);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(providers["onedrive"].Description, Is.EqualTo("Microsoft OneDrive"));
            Assert.That(providers["onedrive"].Overview.Tier, Is.EqualTo("Tier 1"));
            Assert.That(providers["onedrive"].Overview.Virtual, Is.False);
            Assert.That(providers["crypt"].Overview.Virtual, Is.True);
            Assert.That(providers["http"].Overview.IntegrationTests, Is.EqualTo("N/A"));
            Assert.That(providers["tardigrade"].Hide, Is.True);
            Assert.That(providers.ContainsKey("gcs"), Is.True);
        }
    }

    [Test]
    public async Task StartAsync_ReplacesASocketLeftBehind()
    {
        var options = Options();
        await File.WriteAllTextAsync(options.Socket, "");

        await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);

        Assert.That((await rcd.Client.VersionAsync(CancellationToken.None)).Version, Is.EqualTo(BundledRclone.Version));
    }

    [Test]
    public async Task StartAsync_StopsOneLeftRunning()
    {
        var options = Options();
        await using var left = await Rcd.StartAsync(options, CancellationToken.None);
        using var leftProcess = Process.GetProcessById((await left.Client.PidAsync(CancellationToken.None)).Pid);

        await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(leftProcess.HasExited, Is.True);
            Assert.That((await rcd.Client.PidAsync(CancellationToken.None)).Pid, Is.Not.EqualTo(leftProcess.Id));
        }
    }

    [Test]
    public async Task StartAsync_LeavesOutTheUsersRcloneVariables()
    {
        var options = Options();
        Environment.SetEnvironmentVariable("RCLONE_LOG_LEVEL", "DEBUG");
        try
        {
            await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);
            await rcd.Client.VersionAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RCLONE_LOG_LEVEL", null);
        }

        // Only made once it writes
        Assert.That(File.Exists(options.LogFile) ? Log(options.LogFile) : "", Does.Not.Contain("DEBUG"));
    }

    [Test]
    public async Task StartAsync_IgnoresTheUsersProxy()
    {
        var options = Options();
        // What all_proxy or https_proxy set
        var proxy = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = new WebProxy("socks5://127.0.0.1:9");
        try
        {
            await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);

            Assert.That((await rcd.Client.VersionAsync(CancellationToken.None)).Version, Is.EqualTo(BundledRclone.Version));
        }
        finally
        {
            HttpClient.DefaultProxy = proxy;
        }
    }

    [Test]
    public void StartAsync_WhenRcdStopsAtOnce_SaysSo()
    {
        // Longer than a socket's path can be
        var options = Options() with { Socket = Path.Combine(_folder, new string('x', 120) + ".sock") };

        Assert.That(() => Rcd.StartAsync(options, CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.Contains("stopped as it started"));
    }

    [Test]
    public async Task CallAsync_WhenRcloneFails_ThrowsItsMessage_AndItsLogHasIt()
    {
        var options = Options();
        await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);

        Assert.That(() => rcd.Client.CallAsync("no/such", new NoParams(), RcJson.Default.NoParams, RcJson.Default.NoResult, CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.EqualTo("rclone's no/such failed: couldn't find method \"no/such\""));
        // Made when it first writes
        Assert.That(Log(options.LogFile), Does.Contain("couldn't find method \"no/such\""));
    }

    // rclone still has it open
    private static string Log(string file)
    {
        using var log = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return log.ReadToEnd();
    }

    private RcdOptions Options() => new(
        BundledRclone.FilePath,
        Path.Combine(_folder, "rclone.sock"),
        Path.Combine(_folder, "rclone.conf"),
        Path.Combine(_folder, "cache"),
        Path.Combine(_folder, "rclone.log"),
        "OpenBackupManager-Tests");
}
