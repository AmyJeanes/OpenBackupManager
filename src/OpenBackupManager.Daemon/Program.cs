using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Daemon;
using OpenBackupManager.Engine;
#if WINDOWS
using OpenBackupManager.Platform.Windows;
#elif MACOS
using OpenBackupManager.Platform.MacOS;
#elif LINUX
using OpenBackupManager.Platform.Linux;
#endif
using Velopack.Locators;

var homeOption = new Option<string>("--home")
{
    Description = "Keep everything in this folder instead of the usual ones, as the OBM_HOME environment variable does",
    HelpName = "folder",
};

// The tray app starts it with app, so its Quit stops only an engine it started, and obm service install with service
var startedByOption = new Option<EngineStarter>("--started-by") { Hidden = true };

// The service's own definition, which the engine removes if obmd is removed, as nothing else would
var serviceFileOption = new Option<string?>("--service-file") { Hidden = true };

// For obm data remove, which runs it once the engine has stopped
var removeSecretsOption = new Option<bool>("--remove-secrets") { Hidden = true };

var command = new RootCommand("Open Backup Manager's engine, in the foreground with no UI. obm and the app talk to it, and one runs for each home")
{
    homeOption,
    startedByOption,
    serviceFileOption,
    removeSecretsOption,
};
command.SetAction((parseResult, cancellationToken) =>
{
    var paths = AppHome.CommandLinePaths(parseResult.GetValue(homeOption), Installed);
    return parseResult.GetValue(removeSecretsOption)
        ? SecretsRemoval.RunAsync(paths, services => AddPlatform(services, paths), cancellationToken)
        : RunAsync(paths, new EngineOptions(parseResult.GetValue(startedByOption), parseResult.GetValue(serviceFileOption)), cancellationToken);
});

// The host handles Ctrl+C and SIGTERM itself. System.CommandLine's own handling would end the process if stopping took
// over 2 seconds, so it's turned off. Its error handler too, which would print an error and exit rather than let it
// crash, so it's reported
return await command.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null, EnableDefaultExceptionHandler = false });

static async Task<int> RunAsync(AppPaths paths, EngineOptions options, CancellationToken cancellationToken)
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

    // Made first so one it can't make or write to is explained, such as when a file has its name or it belongs to someone
    // else, rather than crashing it
    ProcessLock? taken;
    try
    {
        Directory.CreateDirectory(paths.State);
        taken = ProcessLock.TryTake("engine", paths);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        await Console.Error.WriteLineAsync($"obmd: the engine can't keep its state in {paths.State}. {e.Message}");
        return 1;
    }

    if (taken is null)
    {
        await Console.Error.WriteLineAsync("obmd: the engine is already running");
        return 1;
    }

    using var engineLock = taken;

    IHost built;
    try
    {
        built = EngineHost.Build(paths, options, services => AddPlatform(services, paths));
    }
    catch (ConfigException e)
    {
        await Console.Error.WriteLineAsync($"obmd: {e.Message}. The engine won't start until it's fixed");
        return 1;
    }

    using var host = built;
    var crashes = host.Services.GetRequiredService<CrashCapture>();
    var update = host.Services.GetRequiredService<UpdateWatcher>();
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
    // Such as 75, for its service to restart it into an update
    return update.ExitCode;
}

// Each OS's platform project is only referenced when obmd is built on that OS
static void AddPlatform(IServiceCollection services, AppPaths paths) =>
#if WINDOWS
    services.AddWindowsPlatform(paths);
#elif MACOS
    services.AddMacOSPlatform(paths);
#elif LINUX
    services.AddLinuxPlatform(paths);
#else
    throw new PlatformNotSupportedException("obmd runs on Windows, macOS and Linux");
#endif


// Velopack installs obmd beside the app, so it finds the same install
static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
