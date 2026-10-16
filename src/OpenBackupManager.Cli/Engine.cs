using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli;

// Does nothing yet but run, which the tray app and obm status will connect to
internal sealed partial class Engine(AppPaths paths, ILogger<Engine> logger) : IHostedService
{
    private static readonly string Version =
        typeof(Engine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogStarted(Version, Environment.ProcessId, paths.State);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        LogStopped();
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine {Version} started, process {ProcessId}, keeping its state in {State}")]
    private partial void LogStarted(string version, int processId, string state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine stopped")]
    private partial void LogStopped();
}
