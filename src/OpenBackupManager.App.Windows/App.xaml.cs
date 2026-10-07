using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class App : Application, IDisposable
{
    private Notifications? _notifications;
    private Tray? _tray;
    private Updater? _updater;
    private MainWindow? _window;
    private AboutWindow? _about;

    public App()
    {
        InitializeComponent();
        // The tray keeps the app running with no window open
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _notifications = new Notifications(Open, RestartToUpdate);
        _updater = new Updater(Program.Home, Notifications.ShowUpdateReady, Notifications.ShowUpdated);
        _tray = new Tray(Open, About, Quit);
        if (!Notifications.StartedByClick)
        {
            _tray.ShowFlyout();
        }

        // Raised off the UI thread when the app is launched again
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        AppInstance.GetCurrent().Activated += (_, _) => dispatcher.TryEnqueue(_tray.ShowFlyout);

        // A window app gets no Ctrl+C from the terminal that started it, and dotnet run waits for it to exit. Only for
        // dev builds, since closing the terminal also closes an attached app
        if (!Program.Installed && NativeMethods.AttachToParentConsole())
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                dispatcher.TryEnqueue(Quit);
            };
        }
    }

    // Windows are created when opened and destroyed when closed, so the app stays small while it sits in the tray
    private void Open()
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) => _window = null;
        }

        Show(_window);
    }

    private void About()
    {
        if (_about is null)
        {
            _about = new AboutWindow(_updater!, RestartToUpdate);
            _about.Closed += (_, _) => _about = null;
        }

        Show(_about);
    }

    private static void Show(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
        // Bring app to the front if it was already open behind other windows
        window.SetForegroundWindow();
    }

    public void Dispose()
    {
        _updater?.Dispose();
        _notifications?.Dispose();
        _tray?.Dispose();
    }

    private void Quit()
    {
        Dispose();
        _updater?.UpdateOnExit();
        Exit();
    }

    private void RestartToUpdate()
    {
        if (_updater is not { UpdateReady: true })
        {
            return;
        }

        Dispose();
        _updater.RestartToUpdate();
    }
}
