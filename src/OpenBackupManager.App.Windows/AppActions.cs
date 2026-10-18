using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenBackupManager.Core;
using OpenBackupManager.ViewModels;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class AppActions(IHost host, AppPaths paths, ILogger<AppActions> logger) : IAppActions
{
    private readonly IHost _host = host;
    private readonly AppPaths _paths = paths;
    private readonly ILogger _logger = logger;
    private MainWindow? _window;
    private AboutWindow? _about;
    private bool _quitting;

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

    public void ShowCrash(CrashReport report)
    {
        var window = new CrashWindow(new CrashViewModel(_host.Services.GetRequiredService<AppInfo>(), report));
        Show(window);
    }

    // Async, as stopping the engine waits for it. A second Quit while it does changes nothing
    public async void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        LogQuitting();
        _host.Services.GetRequiredService<CrashReports>().Stopped();
        await _host.Services.GetRequiredService<EngineConnection>().QuitAsync();
        _host.Services.GetRequiredService<Updater>().UpdateOnExit();
        Exit();
    }

    // When Windows signs out or shuts down, it can end the app as soon as this returns, so the clean end is noted first.
    // The rest of quitting follows if there's time, but not installing an update, which Windows could cut off part way.
    // A waiting update installs at the next start instead
    public void EndSession()
    {
        LogEndingSession();
        _host.Services.GetRequiredService<CrashReports>().Stopped();
        SynchronizationContext.Current!.Post(_ => Exit(), null);
    }

    public void RestartToUpdate()
    {
        var updater = _host.Services.GetRequiredService<Updater>();
        if (!updater.UpdateReady)
        {
            return;
        }

        LogRestartingToUpdate(updater.ReadyVersion);
        _host.Services.GetRequiredService<CrashReports>().Stopped();
        _host.Dispose();
        updater.RestartToUpdate();
    }

    // Opens the folder with the file selected, since .toml files often have no app to open them
    public void ShowConfigFile() => Process.Start("explorer.exe", $"/select,\"{_paths.ConfigFile}\"")?.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Quitting")]
    private partial void LogQuitting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Quitting, as Windows is signing out or shutting down")]
    private partial void LogEndingSession();

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping the app's services")]
    private partial void LogStopping();

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarting to install version {Version}")]
    private partial void LogRestartingToUpdate(string? version);

    // Logged first, since logging is one of the services it disposes
    private void Exit()
    {
        LogStopping();
        _host.Dispose();
        Application.Current.Exit();
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
