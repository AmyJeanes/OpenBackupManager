using System.CommandLine;
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

var command = new RootCommand("Open Backup Manager's engine, in the foreground with no UI. obm and the app talk to it, and one runs for each home")
{
    homeOption,
};
command.SetAction((parseResult, cancellationToken) => RunAsync(AppHome.CommandLinePaths(parseResult.GetValue(homeOption), Installed), cancellationToken));

// The host handles Ctrl+C and SIGTERM itself. System.CommandLine's own handling would end the process if stopping took
// over 2 seconds, so it's turned off
return await command.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null });

static async Task<int> RunAsync(AppPaths paths, CancellationToken cancellationToken)
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

    using var host = EngineHost.Build(paths);
    using var shutdownHandler = OperatingSystem.IsWindows() ? new WindowsShutdownHandler(host) : null;
    try
    {
        await host.RunAsync(cancellationToken);
    }
    catch (EngineException e)
    {
        await Console.Error.WriteLineAsync("obmd: " + e.Message);
        return 1;
    }

    return 0;
}

// Velopack installs obmd beside the app, so it finds the same install
static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
