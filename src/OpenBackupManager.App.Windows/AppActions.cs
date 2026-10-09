using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

// What the tray, notifications and windows ask the app to do
public sealed partial class AppActions(IHost host, ILogger<AppActions> logger)
{
    private readonly IHost _host = host;
    private readonly ILogger _logger = logger;
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
        LogQuitting();
        // While the services are still logging
        _host.Services.GetRequiredService<Updater>().UpdateOnExit();
        _host.Dispose();
        Application.Current.Exit();
    }

    public void RestartToUpdate()
    {
        var updater = _host.Services.GetRequiredService<Updater>();
        if (!updater.UpdateReady)
        {
            return;
        }

        LogRestartingToUpdate(updater.ReadyVersion);
        _host.Dispose();
        updater.RestartToUpdate();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Quitting")]
    private partial void LogQuitting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarting to install version {Version}")]
    private partial void LogRestartingToUpdate(string? version);

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
