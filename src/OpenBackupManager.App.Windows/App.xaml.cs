using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using OpenBackupManager.Core;
using OpenBackupManager.Platform.Windows;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows;

public sealed partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        // The tray keeps the app running with no window open
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // Errors on the UI thread come here rather than to .NET's handler
        UnhandledException += (_, e) => services.GetRequiredService<CrashCapture>().Capture(e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var logger = _services.GetRequiredService<ILogger<App>>();
        LogStarted(logger, version, Program.Home);
        if (Program.StartedAtLogin)
        {
            LogStartedAtLogin(logger);
        }

        _ = _services.GetRequiredService<StartAtLoginSetting>();
        // Created here because they need the UI thread. The updater starts checking as it's created
        _ = _services.GetRequiredService<Updater>();
        var tray = _services.GetRequiredService<Tray>();
        _ = _services.GetRequiredService<SessionEnd>();
        _ = _services.GetRequiredService<ConfigNotices>();
        if (_services.GetRequiredService<CrashReports>().TakeNew() is { } crash)
        {
            _services.GetRequiredService<AppActions>().ShowCrash(crash);
        }
        else if (!WindowsNotifier.StartedByClick && !Program.StartedAtLogin)
        {
            tray.ShowFlyout();
        }

        // Raised off the UI thread when the app is launched again
        var ui = SynchronizationContext.Current!;
        AppInstance.GetCurrent().Activated += (_, _) => ui.Post(_ => tray.ShowFlyout(), null);

        // A window app gets no Ctrl+C from the terminal that started it, and dotnet run waits for it to exit. Only for
        // dev builds, since closing the terminal also closes an attached app
        if (!Program.Installed && NativeMethods.AttachToParentConsole())
        {
            var actions = _services.GetRequiredService<IAppActions>();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                ui.Post(_ => actions.Quit(), null);
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started version {Version} with its home in {Home}")]
    private static partial void LogStarted(ILogger logger, string? version, string home);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started at login")]
    private static partial void LogStartedAtLogin(ILogger logger);
}
