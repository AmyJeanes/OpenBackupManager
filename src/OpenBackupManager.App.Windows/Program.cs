using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Platform.Windows;
using OpenBackupManager.ViewModels;
using Serilog;
using Serilog.Events;
using Velopack;
using Velopack.Locators;
using Windows.Win32;

namespace OpenBackupManager.App.Windows;

// One copy runs for each home. Launching the app again hands over to that copy, which opens its flyout
public static class Program
{
    private const string AtLoginArgument = "--at-login";

    // Marks a copy that wasn't installed, such as a build run while developing, so it's easy to tell from the installed app
    internal static string AppName { get; private set; } = typeof(Program).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;

    internal static string Home { get; private set; } = "";

    internal static bool Installed { get; private set; }

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Windows starts the app with this at login, when it stays in the tray rather than opening its flyout
    internal static bool StartedAtLogin { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack's installer runs the app with hook arguments, which are handled here and then exit
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                WindowsNotifier.Unregister();
                RunKey.RemoveFor(Environment.ProcessPath!);
            })
            .Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Installed = VelopackLocator.Current.CurrentlyInstalledVersion is not null;
        if (!Installed)
        {
            AppName += " (dev)";
        }

        // For checking the text, such as qps-ploc for the pseudo-locale
        if (Environment.GetEnvironmentVariable("OBM_UI_CULTURE") is { Length: > 0 } uiCulture)
        {
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
        }

        StartedAtLogin = args.Contains(AtLoginArgument);
        Home = AppHome.Chosen(HomeArgument(args), Environment.GetEnvironmentVariable("OBM_HOME"))
            ?? AppHome.WindowsDefault(Installed, LocalAppData);
        // Off the UI thread, as Microsoft's single-instance guidance does, so the UI thread never blocks on the handover
        if (Task.Run(() => HandOverToRunningCopy(AppHome.InstanceKey(Home), StartedAtLogin)).Result)
        {
            return;
        }

        var host = BuildHost(Home);
        var crashes = host.Services.GetRequiredService<CrashCapture>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => crashes.Capture((Exception)e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => crashes.Unobserved(e.Exception);
        crashes.Start();
        CrashRestart.Register($"--home \"{Home}\"");
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new UiSynchronizationContext(DispatcherQueue.GetForCurrentThread(), crashes));
            _ = new App(host.Services);
        });
    }

    internal static IHost BuildHost(string home)
    {
        var paths = AppPaths.InHome(home);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services
            .AddSingleton(AppInfo.FromAssembly(AppName, typeof(Program).Assembly))
            .AddSingleton(TimeProvider.System)
            .AddSingleton(paths)
            .AddSingleton(_ =>
            {
                var file = new ConfigFile(paths.ConfigFile);
                file.CreateIfMissing();
                return new ConfigWatcher(file);
            })
            .AddSingleton<ConfigNotices>()
            .AddSingleton(services => new CrashReports(paths, services.GetRequiredService<TimeProvider>(), "app-*.log"))
            .AddSingleton<CrashCapture>()
            // App opens the crash window through AppActions itself, which view models never need
            .AddSingleton<AppActions>()
            .AddSingleton<IAppActions>(services => services.GetRequiredService<AppActions>())
            .AddSingleton<INotifier>(_ => new WindowsNotifier(AppName, AppIcons.Ico, $"--home \"{home}\""))
            .AddSingleton<AppNotifications>()
            .AddSingleton<ISecretStore>(_ => new CredentialManager(AppHome.InstanceKey(home)))
            .AddSingleton<IStartAtLogin>(_ => new RunKey(AppHome.InstanceKey(home), $"\"{Environment.ProcessPath}\" --home \"{home}\" {AtLoginArgument}"))
            .AddSingleton(services => new StartAtLoginSetting(
                services.GetRequiredService<ConfigWatcher>(),
                services.GetRequiredService<IStartAtLogin>(),
                AppHome.StartsAtLoginByDefault(home, Installed, LocalAppData),
                services.GetRequiredService<ILogger<StartAtLoginSetting>>()))
            // AppActions installs updates through Updater itself. IUpdater leaves that out, so view models go through
            // AppActions. Both hand out the same Updater, since registering the type again would create a second one
            .AddSingleton<Updater>()
            .AddSingleton<IUpdater>(services => services.GetRequiredService<Updater>())
            .AddSingleton<Tray>()
            .AddSingleton(services => new SessionEnd(services.GetRequiredService<Tray>().Flyout, services.GetRequiredService<AppActions>()))
            .AddSingleton<StatusViewModel>()
            .AddTransient<AboutViewModel>()
            // One file a day, and another once one reaches 10 MB. Keeping the newest 7 is usually a week
            .AddSerilog(log => log
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.File(
                    Path.Combine(paths.Logs, "app-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 7));
        return builder.Build();
    }

    private static string? HomeArgument(string[] args)
    {
        var at = Array.IndexOf(args, "--home");
        return at >= 0 && at < args.Length - 1 ? args[at + 1] : null;
    }

    private static bool HandOverToRunningCopy(string instanceKey, bool atLogin)
    {
        var running = AppInstance.FindOrRegisterForKey(instanceKey);
        if (running.IsCurrent)
        {
            return false;
        }

        // Nothing to open, since nobody launched it
        if (atLogin)
        {
            return true;
        }

        // Windows only lets the app the user just launched take focus, so this passes that on to the running copy's flyout
        PInvoke.AllowSetForegroundWindow(running.ProcessId);
        running.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
        return true;
    }
}
