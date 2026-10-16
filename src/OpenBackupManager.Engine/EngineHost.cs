using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using Serilog;
using Serilog.Events;

namespace OpenBackupManager.Engine;

public static class EngineHost
{
    public static IHost Build(AppPaths paths)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services
            .AddSingleton(paths)
            .AddSingleton(TimeProvider.System)
            .AddSingleton<Listener>()
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
        return builder.Build();
    }
}
