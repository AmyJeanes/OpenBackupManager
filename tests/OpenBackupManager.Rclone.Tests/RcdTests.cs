using System.Diagnostics;

namespace OpenBackupManager.Rclone.Tests;

public class RcdTests
{
    private string _folder = null!;

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-rcd-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, recursive: true);

    [Test]
    public async Task StartAsync_AnswersOnItsSocket_AndQuitsCleanly()
    {
        var options = Options();
        await using (var rcd = await Rcd.StartAsync(options, CancellationToken.None))
        {
            var version = await rcd.Client.VersionAsync(CancellationToken.None);

            Assert.That(version.Version, Is.EqualTo(BundledRclone.Version));
        }

        using (Assert.EnterMultipleScope())
        {
            // A killed rcd leaves its socket behind
            Assert.That(File.Exists(options.Socket), Is.False);
            Assert.That(File.Exists(options.LogFile), Is.True);
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
    public void StartAsync_WhenRcdStopsAtOnce_SaysSo()
    {
        // rclone can't open a log file in a folder that isn't there
        var options = Options() with { LogFile = Path.Combine(_folder, "missing", "rclone.log") };

        Assert.That(() => Rcd.StartAsync(options, CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.Contains("stopped as it started"));
    }

    [Test]
    public async Task CallAsync_WhenRcloneFails_ThrowsItsMessage()
    {
        await using var rcd = await Rcd.StartAsync(Options(), CancellationToken.None);

        Assert.That(() => rcd.Client.CallAsync("no/such", new NoParams(), RcJson.Default.NoParams, RcJson.Default.NoResult, CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.EqualTo("rclone's no/such failed: couldn't find method \"no/such\""));
    }

    private RcdOptions Options() => new(
        BundledRclone.FilePath,
        Path.Combine(_folder, "rclone.sock"),
        Path.Combine(_folder, "rclone.conf"),
        Path.Combine(_folder, "cache"),
        Path.Combine(_folder, "rclone.log"),
        "OpenBackupManager-Tests");
}
