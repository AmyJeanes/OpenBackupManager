using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using Serilog.Events;
using Velopack;
using Velopack.Locators;
using Windows.Win32;

namespace OpenBackupManager.App.Windows;

// One copy runs for each home. Launching the app again hands over to that copy, which opens its flyout
public static class Program
{
    // Marks a copy that wasn't installed, such as a build run while developing, so it's easy to tell from the installed app
    internal static string AppName { get; private set; } = "OpenBackupManager";

    internal static string Home { get; private set; } = "";

    internal static bool Installed { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack's installer runs the app with hook arguments, which are handled here and then exit
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Notifications.Unregister())
            .Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Installed = VelopackLocator.Current.CurrentlyInstalledVersion is not null;
        if (!Installed)
        {
            AppName += " (dev)";
        }

        Home = AppHome.Resolve(
            args,
            Environment.GetEnvironmentVariable("OBM_HOME"),
            Installed,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        // Off the UI thread, as Microsoft's single-instance guidance does, so the UI thread never blocks on the handover
        if (Task.Run(() => HandOverToRunningCopy(AppHome.InstanceKey(Home))).Result)
        {
            return;
        }

        // Quit disposes it
        var host = BuildHost(Home);
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(host.Services);
        });
    }

    internal static IHost BuildHost(string home)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        // So a service that can't be created fails at startup, rather than when it's first used
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services
            .AddSingleton<AppActions>()
            .AddSingleton<Notifications>()
            .AddSingleton<Updater>()
            .AddSingleton<Tray>()
            // One file a day, and another once one reaches 10 MB. Keeping the newest 7 is usually a week
            .AddSerilog(log => log
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.File(
                    Path.Combine(home, "logs", "app-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 7));
        return builder.Build();
    }

    private static bool HandOverToRunningCopy(string instanceKey)
    {
        var running = AppInstance.FindOrRegisterForKey(instanceKey);
        if (running.IsCurrent)
        {
            return false;
        }

        // Windows only lets the app the user just launched take focus, so this passes that on to the running copy's flyout
        PInvoke.AllowSetForegroundWindow(running.ProcessId);
        running.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
        return true;
    }
}
