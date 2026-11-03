using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.Engine;

// Does nothing yet but listen for the tray app and obm. It says it's started once they can connect
internal sealed partial class EngineService(Listener listener, ConfigWatcher config, AppPaths paths, ILogger<EngineService> logger)
    : IHostedService
{
    private PosixSignalRegistration? _hangUp;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await listener.StartAsync();
        // A daemon reads its config again on SIGHUP, which would otherwise end it. Not on Windows, where it means the
        // console is closing
        if (!OperatingSystem.IsWindows())
        {
            _hangUp = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
            {
                context.Cancel = true;
                LogHangUp();
                config.Reload();
            });
        }

        LogStarted(AppVersion.Current, Environment.ProcessId, paths.State);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _hangUp?.Dispose();
        await listener.StopAsync();
        LogStopped();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reading config.toml again, as SIGHUP asks")]
    private partial void LogHangUp();

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine {Version} started, process {ProcessId}, keeping its state in {State}")]
    private partial void LogStarted(string version, int processId, string state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine stopped")]
    private partial void LogStopped();
}
