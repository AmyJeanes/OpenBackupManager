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
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace OpenBackupManager.App.Windows;

// One copy runs for each home. Launching the app again hands over to that copy, which opens its flyout
public static class Program
{
    private const string AtLoginArgument = "--at-login";

    // Marks a copy started again without admin rights
    private const string RestartedArgument = "--restarted";

    // Held until the app ends, as only one copy for each home may start and stop its engine
    private static ProcessLock? _instanceLock;

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
        // Before taking the lock, which the copy started again needs
        if (RunningAs.AdministratorWithNormalTokenToo())
        {
            if (!StartAgainWithoutAdmin(args, Home, DesktopShell.TryStart))
            {
                ShowMessage(Strings.Launch_RunningAsAdministrator);
            }

            return;
        }

        // Global, unlike AppInstance, so it covers every session. Taken on this thread, which runs until the app ends,
        // as a mutex belongs to the thread that took it
        var instanceKey = AppHome.InstanceKey(Home);
        _instanceLock = ProcessLock.TryTake("app", AppPaths.InHome(Home));
        if (_instanceLock is null)
        {
            // Off the UI thread, as Microsoft's single-instance guidance does, so the UI thread never blocks on the handover
            if (!Task.Run(() => HandOverToRunningCopy(instanceKey, StartedAtLogin)).Result && !StartedAtLogin)
            {
                ShowMessage(Strings.Launch_RunningElsewhere);
            }

            return;
        }

        // So the next launch can hand over to this copy
        Task.Run(() => AppInstance.FindOrRegisterForKey(instanceKey)).Wait();

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
            .AddSingleton(services => new EngineConnection(
                home,
                paths,
                services.GetRequiredService<StatusViewModel>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<EngineConnection>>()))
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

    // False if there's no copy to hand over to here, as when it runs in another session or is another exe
    private static bool HandOverToRunningCopy(string instanceKey, bool atLogin)
    {
        // The running copy registers just after it takes the lock, so one launched at the same time may need a moment.
        // Only looks, where FindOrRegisterForKey would register this copy when it finds none
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        AppInstance? running;
        while ((running = AppInstance.GetInstances().FirstOrDefault(i => i.Key == instanceKey)) is null)
        {
            if (DateTime.UtcNow > until)
            {
                return false;
            }

            Thread.Sleep(200);
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

    // As administrator, the engine the app starts refuses to run, and Windows doesn't show the app's notifications. A
    // copy started again with admin rights, as when Explorer has them too, explains rather than starting yet another
    internal static bool StartAgainWithoutAdmin(string[] args, string home, Func<string, string, bool> start)
    {
        if (args.Contains(RestartedArgument))
        {
            return false;
        }

        var arguments = $"--home \"{home}\" {RestartedArgument}";
        return start(Environment.ProcessPath!, args.Contains(AtLoginArgument) ? $"{arguments} {AtLoginArgument}" : arguments);
    }

    // {0} in the message is the app's name
    private static void ShowMessage(string message) => PInvoke.MessageBox(
        HWND.Null,
        Text.Format(message, AppName),
        AppName,
        MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONINFORMATION);
}
