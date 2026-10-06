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
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        // The tray keeps the app running with no window open
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _notifications = new Notifications(Open);
        _tray = new Tray(Open, Quit);
        if (!_notifications.HandleLaunch())
        {
            _tray.ShowFlyout();
        }

        // Raised off the UI thread when the app is launched again
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        AppInstance.GetCurrent().Activated += (_, _) => dispatcher.TryEnqueue(_tray.ShowFlyout);
    }

    // Windows are created when opened and destroyed when closed, so the app stays small while it sits in the tray
    private void Open()
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) => _window = null;
        }

        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        _window.Activate();
        // Bring app to the front if it was already open behind other windows
        _window.SetForegroundWindow();
    }

    public void Dispose()
    {
        _notifications?.Dispose();
        _tray?.Dispose();
    }

    private void Quit()
    {
        Dispose();
        Exit();
    }
}
