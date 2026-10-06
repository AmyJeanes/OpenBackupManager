using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Win32;

namespace OpenBackupManager.App.Windows;

// One copy runs at a time. Launching the app again hands over to that copy, which opens its flyout
public static class Program
{
    private const string InstanceKey = "OpenBackupManager";

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // Off the UI thread, as Microsoft's single-instance guidance does, so the UI thread never blocks on the handover
        if (Task.Run(HandOverToRunningCopy).Result)
        {
            return;
        }

        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }

    private static bool HandOverToRunningCopy()
    {
        var running = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (running.IsCurrent)
        {
            return false;
        }

        // Windows only lets the app the user just launched take focus, so this passes that on to the running copy's flyout
        PInvoke.AllowSetForegroundWindow(running.ProcessId);
        running.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
        return true;
    }
}
