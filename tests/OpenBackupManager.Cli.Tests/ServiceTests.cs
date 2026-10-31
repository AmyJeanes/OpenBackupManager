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
            // CI turns lingering on for its Linux runners
            Assert.That(output, Does.Contain(OperatingSystem.IsLinux() && SystemdService.Lingering ? "starts whenever the computer does" : "starts whenever you sign in"));
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

    // launchd never stops trying, so on macOS installing waits out its whole timeout, longer than Tool.Run does
    [Test]
    [Platform("Linux")]
    public void AnEngineThatCantStart_IsReported_OnceSystemdGivesUp()
    {
        // Held here, so each engine the service starts finds it taken and exits, without ever listening
        Directory.CreateDirectory(_home);
        using var engineLock = new FileStream(Path.Combine(_home, "engine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var watch = Stopwatch.StartNew();

        var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("didn't start the engine").And.Contain(EngineService.ForThisOS(_home)!.HowToCheck));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)));
        }
    }

    [Test]
    public async Task AnEngineStoppedOnPurpose_StaysStopped()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);

        await StopAsync();
        // Longer than launchd waits between starts
        await Task.Delay(TimeSpan.FromSeconds(15));

        Assert.That(await StatusAsync(), Is.Null);
    }

    [Test]
    public async Task Status_WhenTheServiceIsntRunningTheEngine_SaysWhereToLook()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);
        await StopAsync();

        var (exitCode, output) = Tool.Run("obm", "status", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("installed as a service").And.Contain(EngineService.ForThisOS(_home)!.HowToCheck));
        }
    }

    // The usual folders, from XDG folders set only in the shell, which the service doesn't get on its own
    [Test]
    public async Task WithoutAHome_TheEngineUsesTheShellsFolders()
    {
        string[] names = ["XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME"];
        var before = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in names)
        {
            Environment.SetEnvironmentVariable(name, Path.Combine(_home, name));
        }

        try
        {
            var (exitCode, output) = Tool.Run("obm", "service", "install");
            var paths = AppPaths.ForThisOS();
            var status = await StatusAsync(paths);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(File.Exists(EngineService.ForThisOS(null)!.File), Is.True);
                Assert.That(status?.StartedBy, Is.EqualTo(EngineStarter.Service));
                Assert.That(status?.Config, Is.EqualTo(paths.Config));
                Assert.That(status?.State, Is.EqualTo(paths.State));
            }
        }
        finally
        {
            Tool.Run("obm", "service", "uninstall");
            foreach (var (name, value) in before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    // Null if it isn't running
    private async Task<StatusResult?> StatusAsync(AppPaths? paths = null)
    {
        using var engine = await EngineClient.ConnectAsync(paths ?? AppPaths.InHome(_home), TimeSpan.FromSeconds(2), CancellationToken.None);
        return engine is null ? null : await engine.StatusAsync(CancellationToken.None);
    }

    private async Task StopAsync()
    {
        using var engine = await EngineClient.ConnectAsync(AppPaths.InHome(_home), TimeSpan.FromSeconds(2), CancellationToken.None);
        await engine!.StopAsync(CancellationToken.None);
    }
}
