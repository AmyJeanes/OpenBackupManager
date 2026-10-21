using System.CommandLine;
using OpenBackupManager.Cli;
using OpenBackupManager.Core;
using Velopack.Locators;

var homeOption = new Option<string>("--home")
{
    Description = "Keep everything in this folder instead of the usual ones, as the OBM_HOME environment variable does",
    HelpName = "folder",
    Recursive = true,
};

var statusCommand = new Command("status", "Show whether the engine is running, and which");
statusCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Status.RunAsync(Paths(parseResult), ServiceFor(parseResult), cancellationToken));

var installCommand = new Command("install", "Start the engine now and whenever you sign in, and again if it fails");
installCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? (ServiceFor(parseResult) is { } service ? Service.InstallAsync(Paths(parseResult), service, cancellationToken) : OnWindows()));

var uninstallCommand = new Command("uninstall", "Stop the engine, and stop it starting when you sign in");
uninstallCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? (ServiceFor(parseResult) is { } service ? Task.FromResult(Service.Uninstall(service)) : OnWindows()));

// Not on Windows, where the app starts the engine instead, but it still says so to anyone who tries it
var serviceCommand = new Command("service", "Keep the engine running from when you sign in, through the OS's own service manager")
{
    installCommand,
    uninstallCommand,
};
serviceCommand.Hidden = OperatingSystem.IsWindows();

var command = new RootCommand("Open Backup Manager's command line")
{
    homeOption,
    statusCommand,
    serviceCommand,
};

return await command.Parse(args).InvokeAsync();

AppPaths Paths(ParseResult parseResult) => AppHome.CommandLinePaths(parseResult.GetValue(homeOption), Installed);

// The service is given the home as a path, as it gets none of the shell's environment
EngineService? ServiceFor(ParseResult parseResult) =>
    EngineService.ForThisOS(AppHome.Chosen(parseResult.GetValue(homeOption), Environment.GetEnvironmentVariable("OBM_HOME")));

// Checked in each command, so --help and --version still work. An elevated obm on Windows reaches the user's engine,
// so it only matters elsewhere
static Task<int>? RefuseUnderSudo()
{
    if (OperatingSystem.IsWindows() || !RunningAs.SwitchedFromAnotherUser())
    {
        return null;
    }

    Console.Error.WriteLine("obm: sudo or similar started this as another user, so it would look for their engine instead of yours. Run it again without sudo");
    return Task.FromResult(1);
}

static Task<int> OnWindows()
{
    Console.Error.WriteLine(Service.OnWindows);
    return Task.FromResult(1);
}

// Velopack installs obm beside the app, so it finds the same install
static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
