using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;
using Serilog;
using Serilog.Events;

namespace OpenBackupManager.Engine;

public static class EngineHost
{
    public static IHost Build(AppPaths paths, EngineStarter startedBy = EngineStarter.Other, Action<IServiceCollection>? platform = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services
            .AddSingleton(paths)
            .AddSingleton(new EngineOptions(startedBy))
            .AddSingleton(TimeProvider.System)
            .AddSingleton(services => new CrashReports(paths, services.GetRequiredService<TimeProvider>(), CrashReports.Engine))
            .AddSingleton<CrashCapture>()
            .AddSingleton(services => new ConfigWatcher(new ConfigFile(paths.ConfigFile), services.GetRequiredService<ILogger<ConfigWatcher>>()))
            .AddSingleton(new FileStore(Path.Combine(paths.State, "secrets.json")))
            .AddKeyedSingleton<ISecretStore>(SecretStoreKind.File, (services, _) => services.GetRequiredService<FileStore>())
            .AddSingleton<Secrets>()
            .AddSingleton<Listener>()
            // Before the engine, as they stop in reverse, so the engine stops answering first
            .AddSingleton<UpdateWatcher>()
            .AddHostedService(services => services.GetRequiredService<UpdateWatcher>())
            .AddHostedService<EngineService>()
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
        platform?.Invoke(builder.Services); // OS-specific implementations

        var host = builder.Build();
        try
        {
            // Read config on startup to crash early if there's an error in it
            host.Services.GetRequiredService<ConfigWatcher>();
        }
        catch (ConfigException)
        {
            host.Dispose();
            throw;
        }

        return host;
    }
}
