using System.CommandLine;
using OpenBackupManager.Cli;

var homeOption = new Option<string>("--home")
{
    Description = "Keep everything in this folder instead of the usual ones, as the OBM_HOME environment variable does",
    HelpName = "folder",
    Recursive = true,
};

var daemonCommand = new Command("daemon", "Run the engine with no UI, in the foreground");
daemonCommand.SetAction((parseResult, cancellationToken) => Daemon.RunAsync(Home.Paths(parseResult.GetValue(homeOption)), cancellationToken));

var command = new RootCommand("Open Backup Manager's command line")
{
    homeOption,
    daemonCommand,
};

// The daemon's host handles Ctrl+C and SIGTERM itself. System.CommandLine's own handling would end the process if
// stopping took over 2 seconds, so it's turned off
return await command.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null });
