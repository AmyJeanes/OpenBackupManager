using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using Serilog;
using Serilog.Events;

namespace OpenBackupManager.Cli;

// obm daemon: the engine, with no UI. One runs for each home
internal static class Daemon
{
    public static async Task<int> RunAsync(AppPaths paths, CancellationToken cancellationToken)
    {
        using var engineLock = ProcessLock.TryTake("engine", paths);
        if (engineLock is null)
        {
            await Console.Error.WriteLineAsync("obm: the engine is already running");
            return 1;
        }

        using var host = BuildHost(paths);
        using var shutdownHandler = OperatingSystem.IsWindows() ? new WindowsShutdownHandler(host) : null;
        await host.RunAsync(cancellationToken);
        return 0;
    }

    internal static IHost BuildHost(AppPaths paths)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services
            .AddSingleton(paths)
            .AddHostedService<Engine>()
            // The same files as the app's, under their own name. The console gets the same lines, for running it by hand
            // and for service managers that keep what a service prints
            .AddSerilog(log => log
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture, outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(paths.Logs, "engine-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 7));
        return builder.Build();
    }
}
