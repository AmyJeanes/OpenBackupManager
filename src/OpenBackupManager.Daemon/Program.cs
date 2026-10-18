using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Daemon;
using OpenBackupManager.Engine;
using Velopack.Locators;

var homeOption = new Option<string>("--home")
{
    Description = "Keep everything in this folder instead of the usual ones, as the OBM_HOME environment variable does",
    HelpName = "folder",
};

// The tray app starts it with this, so its Quit stops only an engine it started
var startedByAppOption = new Option<bool>("--started-by-app") { Hidden = true };

var command = new RootCommand("Open Backup Manager's engine, in the foreground with no UI. obm and the app talk to it, and one runs for each home")
{
    homeOption,
    startedByAppOption,
};
command.SetAction((parseResult, cancellationToken) => RunAsync(
    AppHome.CommandLinePaths(parseResult.GetValue(homeOption), Installed),
    parseResult.GetValue(startedByAppOption),
    cancellationToken));

// The host handles Ctrl+C and SIGTERM itself. System.CommandLine's own handling would end the process if stopping took
// over 2 seconds, so it's turned off. Its error handler too, which would print an error and exit rather than let it
// crash, so it's reported
return await command.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null, EnableDefaultExceptionHandler = false });

static async Task<int> RunAsync(AppPaths paths, bool startedByApp, CancellationToken cancellationToken)
{
    if (OperatingSystem.IsWindows() && RunningAs.AdministratorWithNormalTokenToo())
    {
        await Console.Error.WriteLineAsync("obmd: this is running as administrator, so the tray app and obm in a normal terminal couldn't reach the engine. Run it again from a normal terminal");
        return 1;
    }

    if (!OperatingSystem.IsWindows() && RunningAs.SwitchedFromAnotherUser())
    {
        await Console.Error.WriteLineAsync("obmd: sudo or similar started this as another user, so the engine would run as them instead of you. Run it again without sudo");
        return 1;
    }

    using var engineLock = ProcessLock.TryTake("engine", paths);
    if (engineLock is null)
    {
        await Console.Error.WriteLineAsync("obmd: the engine is already running");
        return 1;
    }

    using var host = EngineHost.Build(paths, startedByApp);
    var crashes = host.Services.GetRequiredService<CrashCapture>();
    crashes.Start();
    using var shutdownHandler = OperatingSystem.IsWindows() ? new WindowsShutdownHandler(host) : null;
    try
    {
        await host.RunAsync(cancellationToken);
    }
    catch (EngineException e)
    {
        crashes.Stopped();
        await Console.Error.WriteLineAsync("obmd: " + e.Message);
        return 1;
    }

    // Before the shutdown handler lets Windows end the process
    crashes.Stopped();
    return 0;
}

// Velopack installs obmd beside the app, so it finds the same install
static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
