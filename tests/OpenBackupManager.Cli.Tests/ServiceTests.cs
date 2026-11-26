// Left out on Windows rather than skipped, or the Sandbox filter would run none of this project's explicit tests
#if !WINDOWS
using System.Diagnostics;
using System.Runtime.Versioning;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

// obm service install with the OS's real service manager, for a home of its own
[Explicit("Installs the engine with the OS's service manager")]
[Category("Sandbox")]
[UnsupportedOSPlatform("windows")]
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
        var engine = (await StatusAsync())!;

        var (exitCode, output) = Tool.Run("obm", "service", "uninstall", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("no longer starts when you sign in"));
            Assert.That(File.Exists(EngineService.ForThisOS(_home)!.File), Is.False);
            Assert.That(Running(engine.ProcessId), Is.False, "The engine was still running");
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

    // As a .deb or .rpm does when it's upgraded
    [Test]
    public async Task AnUpdate_RestartsTheEngine()
    {
        var programs = Path.Combine(_home, "programs");
        Tool.CopyTo(programs);
        Assert.That(Tool.Run(Path.Combine(programs, "obm"), "service", "install", "--home", _home).ExitCode, Is.Zero);
        var first = (await StatusAsync())!;

        Tool.Update(Path.Combine(programs, "obmd"));

        // launchd waits 10 seconds between starts
        var until = DateTime.UtcNow + Timeout;
        StatusResult? next = null;
        while (DateTime.UtcNow < until)
        {
            try
            {
                next = await StatusAsync();
            }
            // The old one, still stopping, takes a connection and closes it
            catch (EngineException)
            {
                next = null;
            }

            if (next is not null && next.ProcessId != first.ProcessId)
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.That(next?.ProcessId, Is.Not.Null.And.Not.EqualTo(first.ProcessId), "The engine wasn't restarted");
    }

    [Test]
    public async Task ObmUpdate_WaitsForTheServiceToRestartTheEngine()
    {
        var programs = Path.Combine(_home, "programs");
        Tool.CopyTo(programs);
        var obm = Path.Combine(programs, "obm");
        Assert.That(Tool.Run(obm, "service", "install", "--home", _home).ExitCode, Is.Zero);
        var first = (await StatusAsync())!;
        using var github = new FakeGitHub();
        github.Add("0.3.0", new Asset($"OpenBackupManager-0.3.0-{FakeGitHub.Runtime}.tar.gz", FakeGitHub.NewVersionOf(programs)));

        var (exitCode, output) = Tool.Run(obm, github.Environment, "update", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("Waiting for the service to restart the engine"));
            Assert.That(output, Does.Contain("The engine restarted and is running "));
            Assert.That((await StatusAsync())?.ProcessId, Is.Not.EqualTo(first.ProcessId));
        }
    }

    // As removing the .deb or .rpm does, without obm service uninstall first
    [Test]
    public async Task RemovingObmd_RemovesTheService()
    {
        var programs = Path.Combine(_home, "programs");
        Tool.CopyTo(programs);
        Assert.That(Tool.Run(Path.Combine(programs, "obm"), "service", "install", "--home", _home).ExitCode, Is.Zero);
        var service = EngineService.ForThisOS(_home)!;

        File.Delete(Path.Combine(programs, "obmd"));

        var until = DateTime.UtcNow + Timeout;
        while ((File.Exists(service.File) || !await GoneAsync()) && DateTime.UtcNow < until)
        {
            await Task.Delay(500);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.File, Does.Not.Exist);
            Assert.That(await GoneAsync(), Is.True, "The engine kept running");
        }
    }

    [Test]
    public async Task DataRemove_UninstallsTheService()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);

        var (exitCode, output) = Tool.Run("obm", "data", "remove", "--home", _home, "--yes");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("The engine has stopped, and no longer starts when you sign in"));
            Assert.That(EngineService.ForThisOS(_home)!.File, Does.Not.Exist);
            Assert.That(await StatusAsync(), Is.Null);
            Assert.That(_home, Does.Not.Exist);
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

    // Its engine wouldn't start, and the service manager would keep starting it
    [Test]
    public void Install_WithAnErrorInConfigToml_SaysSo_AndInstallsNothing()
    {
        var config = Path.Combine(_home, "config.toml");
        Directory.CreateDirectory(_home);
        File.WriteAllText(config, "version = 1\n[updates]\nprereleases = \"yes\"\n");

        var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain($"obm: {config} has an error on line 3, so the engine wouldn't start: updates.prereleases should be true or false"));
            Assert.That(File.Exists(EngineService.ForThisOS(_home)!.File), Is.False);
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

    // As in most containers, or under WSL with systemd off, where systemctl is there but can't reach systemd
    [Test]
    [Platform("Linux")]
    public void WithoutSystemd_SaysSo_AndLeavesNothingBehind()
    {
        var file = EngineService.ForThisOS(_home)!.File;
        using var runtime = new EnvironmentVariable("XDG_RUNTIME_DIR", Path.Combine(_home, "nowhere"));
        using var bus = new EnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", null);

        var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("systemd isn't present or can't be reached"));
            Assert.That(File.Exists(file), Is.False);
        }
    }

    // A folder in its place stands in for one it can't write to, as when a tool run with sudo left its folder belonging
    // to root, without changing the folder other services are in
    [Test]
    public void Install_WhereItsFileCantBeWritten_SaysSo_AndLeavesAnEngineStartedByHandRunning()
    {
        var file = EngineService.ForThisOS(_home)!.File;
        Directory.CreateDirectory(file);
        try
        {
            using var byHand = Tool.Start("obmd", "--home", _home);
            byHand.WaitFor("started, process");

            var (exitCode, output) = Tool.Run("obm", "service", "install", "--home", _home);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(output, Does.StartWith($"obm: {file} can't be changed").And.Not.Contain("Unhandled exception"));
                Assert.That(byHand.HasExited, Is.False, "The engine was stopped");
            }
        }
        finally
        {
            Directory.Delete(file);
        }
    }

    // As when the engine was stopped and then started by hand, which would otherwise keep running
    [Test]
    public async Task Uninstall_StopsAnEngineStartedAnotherWay()
    {
        Assert.That(Tool.Run("obm", "service", "install", "--home", _home).ExitCode, Is.Zero);
        await StopAsync();
        using var byHand = Tool.Start("obmd", "--home", _home);
        byHand.WaitFor("started, process");

        var (exitCode, output) = Tool.Run("obm", "service", "uninstall", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("The engine has stopped"));
            Assert.That(byHand.HasExited, Is.True, "The engine was still running");
        }
    }

    // systemd would otherwise keep listing it as failed, and say the user's services are degraded
    [Test]
    [Platform("Linux")]
    public void Uninstall_OnceSystemdHasGivenUp_LeavesNothingFailed()
    {
        // Held here, so each engine the service starts finds it taken and exits
        Directory.CreateDirectory(_home);
        using var engineLock = new FileStream(Path.Combine(_home, "engine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var service = EngineService.ForThisOS(_home)!;
        Tool.Run("obm", "service", "install", "--home", _home);
        Assert.That(service.HasGivenUp, Is.True, "systemd hadn't given up on it");

        var (exitCode, output) = Tool.Run("obm", "service", "uninstall", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(service.HasGivenUp, Is.False, "systemd still lists it as failed");
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
        // Kept until after the service is uninstalled, which finds it through them
        using var config = new EnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_home, "XDG_CONFIG_HOME"));
        using var state = new EnvironmentVariable("XDG_STATE_HOME", Path.Combine(_home, "XDG_STATE_HOME"));
        using var cache = new EnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_home, "XDG_CACHE_HOME"));
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
        }
    }

    // Null if it isn't running
    private async Task<StatusResult?> StatusAsync(AppPaths? paths = null)
    {
        using var engine = await EngineClient.ConnectAsync(paths ?? AppPaths.InHome(_home), TimeSpan.FromSeconds(2), CancellationToken.None);
        return engine is null ? null : await engine.StatusAsync(CancellationToken.None);
    }

    // Not while it's still stopping, when it takes a connection and closes it
    private async Task<bool> GoneAsync()
    {
        try
        {
            return await StatusAsync() is null;
        }
        catch (EngineException)
        {
            return false;
        }
    }

    // Until it exits, as one still stopping takes a connection and closes it
    private async Task StopAsync()
    {
        using var engine = await EngineClient.ConnectAsync(AppPaths.InHome(_home), TimeSpan.FromSeconds(2), CancellationToken.None);
        using var process = Process.GetProcessById((await engine!.StatusAsync(CancellationToken.None)).ProcessId);
        await engine.StopAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(Timeout);
        await process.WaitForExitAsync(timeout.Token);
    }

    private static bool Running(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
#endif
