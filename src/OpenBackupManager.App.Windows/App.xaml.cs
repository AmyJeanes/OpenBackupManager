using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

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
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Created here because they need the UI thread. The updater starts checking as it's created
        _ = _services.GetRequiredService<Updater>();
        var tray = _services.GetRequiredService<Tray>();
        if (!Notifications.StartedByClick)
        {
            tray.ShowFlyout();
        }

        // Raised off the UI thread when the app is launched again
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        AppInstance.GetCurrent().Activated += (_, _) => dispatcher.TryEnqueue(tray.ShowFlyout);

        // A window app gets no Ctrl+C from the terminal that started it, and dotnet run waits for it to exit. Only for
        // dev builds, since closing the terminal also closes an attached app
        if (!Program.Installed && NativeMethods.AttachToParentConsole())
        {
            var actions = _services.GetRequiredService<AppActions>();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                dispatcher.TryEnqueue(actions.Quit);
            };
        }
    }
}
