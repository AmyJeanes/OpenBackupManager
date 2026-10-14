using Microsoft.UI.Xaml;
using Windows.Win32;
using WinUIEx.Messaging;

namespace OpenBackupManager.App.Windows;

// Windows' notice that it's signing out or shutting down. A window app gets no signal as a console one does, only this
// message to each of its top-level windows, after which Windows ends it. The flyout's window hears it, since it's open
// for as long as the app runs, while the tray icon's window is message-only and doesn't
public sealed class SessionEnd : IDisposable
{
    private readonly WindowMessageMonitor _monitor;
    private readonly AppActions _actions;

    public SessionEnd(Window window, AppActions actions)
    {
        _actions = actions;
        _monitor = new WindowMessageMonitor(window);
        _monitor.WindowMessageReceived += OnMessage;
    }

    public void Dispose() => _monitor.Dispose();

    // Its WParam is 0 when the session isn't ending after all, such as when another app refused
    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == PInvoke.WM_ENDSESSION && e.Message.WParam != 0)
        {
            _actions.EndSession();
        }
    }
}
