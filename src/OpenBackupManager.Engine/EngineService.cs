using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;

namespace OpenBackupManager.Engine;

// Does nothing yet but listen for the tray app and obm. It says it's started once they can connect
internal sealed partial class EngineService(Listener listener, AppPaths paths, ILogger<EngineService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await listener.StartAsync();
        LogStarted(AppVersion.Current, Environment.ProcessId, paths.State);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await listener.StopAsync();
        LogStopped();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine {Version} started, process {ProcessId}, keeping its state in {State}")]
    private partial void LogStarted(string version, int processId, string state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine stopped")]
    private partial void LogStopped();
}
