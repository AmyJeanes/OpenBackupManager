using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

// What the tray, notifications and windows ask the app to do
public sealed class AppActions(IHost host)
{
    private readonly IHost _host = host;
    private MainWindow? _window;
    private AboutWindow? _about;

    // Windows are created when opened and destroyed when closed, so the app stays small while it sits in the tray
    public void Open()
    {
        if (_window is null)
        {
            _window = ActivatorUtilities.CreateInstance<MainWindow>(_host.Services);
            _window.Closed += (_, _) => _window = null;
        }

        Show(_window);
    }

    public void About()
    {
        if (_about is null)
        {
            _about = ActivatorUtilities.CreateInstance<AboutWindow>(_host.Services);
            _about.Closed += (_, _) => _about = null;
        }

        Show(_about);
    }

    public void Quit()
    {
        var updater = _host.Services.GetRequiredService<Updater>();
        _host.Dispose();
        updater.UpdateOnExit();
        Application.Current.Exit();
    }

    public void RestartToUpdate()
    {
        var updater = _host.Services.GetRequiredService<Updater>();
        if (!updater.UpdateReady)
        {
            return;
        }

        _host.Dispose();
        updater.RestartToUpdate();
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
}
