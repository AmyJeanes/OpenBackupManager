using System.CommandLine;
using OpenBackupManager.Cli;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
using Velopack.Locators;

var homeOption = new Option<string>("--home")
{
    Description = "Keep everything in this folder instead of the usual ones, as the OBM_HOME environment variable does",
    HelpName = "folder",
    Recursive = true,
};

// On each command that reports something, not the root, so help only offers it where it does something
var jsonOption = new Option<bool>("--json") { Description = "Print it as JSON, for scripts" };

var statusCommand = new Command("status", "Show whether the engine is running, and which, and any new crash reports") { jsonOption };
statusCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Status.RunAsync(Paths(parseResult), ServiceFor(parseResult), parseResult.GetValue(jsonOption), cancellationToken));

var installCommand = new Command("install", "Start the engine now and whenever you sign in, and restart if it fails");
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

var storeArgument = new Argument<string>("store") { Description = "The store to keep them in from now on" };
storeArgument.AcceptOnlyFromAmong([.. SecretStoreKinds.NamesOn(CurrentOS.Platform)]);
var migrateCommand = new Command("migrate", "Move this device's secrets to another store, and keep them there") { storeArgument };
migrateCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Migrate.RunAsync(Paths(parseResult), parseResult.GetValue(storeArgument)!, cancellationToken));

var secretsCommand = new Command("secrets", "Where this device keeps its secrets, such as the password that protects your sign-ins")
{
    migrateCommand,
};

var reloadCommand = new Command("reload", "Have the engine read config.toml again, for when it can't see the file change, such as on a network drive")
{
    jsonOption,
};
reloadCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Config.ReloadAsync(Paths(parseResult), parseResult.GetValue(jsonOption), cancellationToken));

var fileOption = new Option<string?>("--file")
{
    Description = "Check this file instead of config.toml",
    HelpName = "file",
};
var envOption = new Option<bool>("--env")
{
    Description = "Also apply this shell's OBM_ environment variables",
};
var validateCommand = new Command("validate", "Check config.toml for errors and warnings, without the engine")
{
    fileOption,
    envOption,
    jsonOption,
};
validateCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Task.FromResult(Config.Validate(Paths(parseResult), parseResult.GetValue(fileOption), parseResult.GetValue(envOption), parseResult.GetValue(jsonOption))));

var configCommand = new Command("config", "This device's settings, in config.toml")
{
    reloadCommand,
    validateCommand,
};

var listCommand = new Command("list", "List the crash reports, newest first") { jsonOption };
listCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Task.FromResult(Crashes.List(Paths(parseResult), parseResult.GetValue(jsonOption))));

var numberArgument = new Argument<int>("number")
{
    Description = "Which, as obm crashes list numbers them",
    Arity = ArgumentArity.ZeroOrOne,
    DefaultValueFactory = _ => 1,
};
var showCommand = new Command("show", "Show a crash report, the newest unless you say which, and count it and older ones as seen")
{
    numberArgument,
};
showCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Task.FromResult(Crashes.Show(Paths(parseResult), parseResult.GetValue(numberArgument))));

var noBrowserOption = new Option<bool>("--no-browser")
{
    Description = "Print the link rather than opening it",
};
var reportCommand = new Command("report", "Open a new GitHub issue with a crash report filled in, the newest unless you say which")
{
    numberArgument,
    noBrowserOption,
};
reportCommand.SetAction((parseResult, cancellationToken) =>
    RefuseUnderSudo() ?? Task.FromResult(Crashes.Report(Paths(parseResult), parseResult.GetValue(numberArgument), parseResult.GetValue(noBrowserOption))));

var crashesCommand = new Command("crashes", "Reports of the app or the engine closing unexpectedly")
{
    listCommand,
    showCommand,
    reportCommand,
};

var command = new RootCommand("Open Backup Manager's command line")
{
    homeOption,
    statusCommand,
    serviceCommand,
    secretsCommand,
    configCommand,
    crashesCommand,
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
    Console.Error.WriteLine("obm: on Windows, the app starts the engine when you sign in. To run the engine without the app, start obmd with Task Scheduler instead");
    return Task.FromResult(1);
}

// Velopack installs obm beside the app, so it finds the same install
static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
