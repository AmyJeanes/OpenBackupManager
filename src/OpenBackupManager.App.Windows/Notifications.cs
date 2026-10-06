using System.Security;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace OpenBackupManager.App.Windows;

// Windows notifications. Their buttons come back to this copy of the app, or start it if it isn't running
public sealed class Notifications : IDisposable
{
    private const string ViewDetails = "viewDetails";
    private const string Restart = "restart";

    private readonly Action _open;
    private readonly Action _restartToUpdate;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public Notifications(Action open, Action restartToUpdate)
    {
        _open = open;
        _restartToUpdate = restartToUpdate;
        // Subscribed before registering, or Windows starts another copy to handle each click
        AppNotificationManager.Default.NotificationInvoked += OnInvoked;
        // Named here, or Windows shows the exe's name
        AppNotificationManager.Default.Register(Program.AppName, new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")));
    }

    public static void ShowTest() => AppNotificationManager.Default.Show(new AppNotificationBuilder()
        .AddArgument("action", ViewDetails)
        .AddText("Sync failed")
        .AddText("Test folder couldn't sync (test notification)")
        .AddButton(Button("View details").AddArgument("action", ViewDetails))
        .BuildNotification());

    public static void ShowUpdateReady(string version) => AppNotificationManager.Default.Show(new AppNotificationBuilder()
        .AddText("Update available")
        .AddText($"Version {version} is ready to install")
        .AddButton(Button("Restart now").AddArgument("action", Restart))
        .BuildNotification()
        .WithDismissButton("Not now"));

    public static void ShowUpdated(string version) => AppNotificationManager.Default.Show(new AppNotificationBuilder()
        .AddText("Update installed")
        .AddText($"Version {version} is now installed")
        .AddButton(Button("What's new").SetInvokeUri(new Uri($"{Updater.RepositoryUrl}/releases/tag/v{version}")))
        .BuildNotification());

    // The builder writes a button's label into the XML without escaping it, so an apostrophe or & would break the
    // notification. Its text lines are escaped. Remove once https://github.com/microsoft/WindowsAppSDK/issues/4996 is fixed
    private static AppNotificationButton Button(string label) => new(SecurityElement.Escape(label));

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
        args.Arguments.TryGetValue("action", out var action);
        switch (action)
        {
            case ViewDetails:
                _dispatcher.TryEnqueue(() => _open());
                break;
            case Restart:
                _dispatcher.TryEnqueue(() => _restartToUpdate());
                break;
        }
    }
}
