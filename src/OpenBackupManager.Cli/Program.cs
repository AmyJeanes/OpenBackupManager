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
statusCommand.SetAction((parseResult, cancellationToken) => Status.RunAsync(AppHome.CommandLinePaths(parseResult.GetValue(homeOption), Installed), cancellationToken));

var command = new RootCommand("Open Backup Manager's command line")
{
    homeOption,
    statusCommand,
};

return await command.Parse(args).InvokeAsync();

// Velopack installs obm beside the app, so it finds the same install
static bool Installed() => VelopackLocator.CreateDefaultForPlatform().CurrentlyInstalledVersion is not null;
