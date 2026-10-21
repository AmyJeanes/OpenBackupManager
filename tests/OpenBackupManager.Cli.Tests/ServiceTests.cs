using System.Diagnostics;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

// obm service install with the OS's real service manager, for a home of its own
[Explicit("Installs the engine with the OS's service manager")]
[Category("Sandbox")]
[Platform(Exclude = "Win")]
public class ServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private string _home = null!;

    [SetUp]
    public void SetUp() => _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [TearDown]
    public void TearDown()
    {
        Tool.Run("obm", "service", "uninstall", "--home", _home);
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Test]
    public async Task Install_StartsTheEngine_AsAService()
    {
        var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("starts whenever you sign in"));
            Assert.That(File.Exists(EngineService.ForThisOS(_home)!.File), Is.True);
            Assert.That((await StatusAsync())?.StartedBy, Is.EqualTo(EngineStarter.Service));
            Assert.That(Tool.Run("obm", "status", "--home", _home).Output, Does.Contain("Started by: its service"));
        }
    }

    [Test]
    public async Task Uninstall_StopsTheEngine_AndRemovesIt()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);

        var (exitCode, output) = Tool.Run("obm", "service", "uninstall", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("no longer starts when you sign in"));
            Assert.That(File.Exists(EngineService.ForThisOS(_home)!.File), Is.False);
            Assert.That(await StatusAsync(), Is.Null);
        }
    }

    [Test]
    public async Task AnEngineThatDies_IsStartedAgain()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);
        var first = (await StatusAsync())!;

        using (var process = Process.GetProcessById(first.ProcessId))
        {
            process.Kill();
            await process.WaitForExitAsync();
        }

        // launchd waits 10 seconds between starts
        var until = DateTime.UtcNow + Timeout;
        StatusResult? next;
        while ((next = await StatusAsync()) is null && DateTime.UtcNow < until)
        {
            await Task.Delay(500);
        }

        Assert.That(next, Is.Not.Null, "The engine wasn't started again");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(next!.ProcessId, Is.Not.EqualTo(first.ProcessId));
            Assert.That(next.StartedBy, Is.EqualTo(EngineStarter.Service));
        }
    }

    [Test]
    public async Task Install_TakesOverAnEngineAlreadyRunning()
    {
        using var byHand = Tool.Start("obmd", "--home", _home);
        byHand.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("Stopping the engine that's running"));
            Assert.That(byHand.HasExited, Is.True);
            Assert.That((await StatusAsync())?.StartedBy, Is.EqualTo(EngineStarter.Service));
        }
    }

    [Test]
    public async Task InstallingAgain_StartsTheEngineAgain()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);
        var first = (await StatusAsync())!;

        var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Not.Contain("Stopping the engine"));
            Assert.That((await StatusAsync())?.ProcessId, Is.Not.EqualTo(first.ProcessId));
        }
    }

    // Null if it isn't running
    private async Task<StatusResult?> StatusAsync()
    {
        using var engine = await EngineClient.ConnectAsync(AppPaths.InHome(_home), TimeSpan.FromSeconds(2), CancellationToken.None);
        return engine is null ? null : await engine.StatusAsync(CancellationToken.None);
    }
}
