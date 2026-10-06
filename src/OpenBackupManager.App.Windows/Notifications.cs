using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace OpenBackupManager.App.Windows;

// Windows notifications. Their buttons come back to this copy of the app, or start it if it isn't running
public sealed class Notifications : IDisposable
{
    private const string ViewDetails = "viewDetails";

    private readonly Action _open;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public Notifications(Action open)
    {
        _open = open;
        // Subscribed before registering, or Windows starts another copy to handle each click
        AppNotificationManager.Default.NotificationInvoked += OnInvoked;
        // Named here, or Windows shows the exe's name
        AppNotificationManager.Default.Register("OpenBackupManager", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")));
    }

    public static void ShowTest() => AppNotificationManager.Default.Show(new AppNotificationBuilder()
        .AddArgument("action", ViewDetails)
        .AddText("Sync failed")
        .AddText("Test folder couldn't sync (test notification)")
        .AddButton(new AppNotificationButton("View details").AddArgument("action", ViewDetails))
        .BuildNotification());

    // A click that started the app arrives with the launch rather than as an event. True if there was one
    public bool HandleLaunch()
    {
        var launch = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (launch.Kind != ExtendedActivationKind.AppNotification)
        {
            return false;
        }

        OnInvoked(AppNotificationManager.Default, (AppNotificationActivatedEventArgs)launch.Data);
        return true;
    }

    public void Dispose() => AppNotificationManager.Default.Unregister();

    // Raised off the UI thread
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("action", out var action) && action == ViewDetails)
        {
            _dispatcher.TryEnqueue(() => _open());
        }
    }
}
