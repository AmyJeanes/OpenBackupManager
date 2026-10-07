using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace OpenBackupManager.App.Windows;

// Windows notifications. Their buttons come back to this copy of the app, or start it if it isn't running
public sealed class Notifications : IDisposable
{
    private const string ViewDetails = "viewDetails";
    private const string Restart = "restart";

    // Windows groups notifications by the app's ID. Velopack gives the installed app its Start menu shortcut's ID,
    // and a copy that wasn't installed gets one from its path, so copies in different folders keep their own
    private static readonly string AppId = NativeMethods.ExplicitAppId()
        ?? "OpenBackupManager.Dev." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.ProcessPath!.ToUpperInvariant())))[..16];

    // The activator's COM class, made from the app's ID so each copy keeps the same one
    private static readonly Guid ActivatorId = new(SHA256.HashData(Encoding.UTF8.GetBytes(AppId))[..16]);

    private readonly Action _open;
    private readonly Action _restartToUpdate;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly uint _activator;

    public Notifications(Action open, Action restartToUpdate)
    {
        _open = open;
        _restartToUpdate = restartToUpdate;
        Register();
        _activator = NotificationActivator.Register(ActivatorId, OnActivated);
    }

    // Windows starts the app this way to deliver a click when it isn't running, and the click then reaches the activator
    public static bool StartedByClick => Environment.GetCommandLineArgs().Contains("-Embedding");

    public static void ShowTest() => Show(new Toast(ViewDetails)
        .Text("Sync failed")
        .Text("Test folder couldn't sync (test notification)")
        .Button("View details", ViewDetails));

    public static void ShowUpdateReady(string version) => Show(new Toast()
        .Text("Update available")
        .Text($"Version {version} is ready to install")
        .Button("Restart now", Restart)
        .DismissButton("Not now"));

    public static void ShowUpdated(string version) => Show(new Toast()
        .Text("Update installed")
        .Text($"Version {version} is now installed")
        .Link("What's new", new Uri($"{Updater.RepositoryUrl}/releases/tag/v{version}")));

    // When uninstalling, so nothing is left behind
    public static void Unregister()
    {
        ToastNotificationManager.History.Clear(AppId);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{AppId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{ActivatorId:B}", throwOnMissingSubKey: false);
    }

    // Clicks after this start the app again
    public void Dispose() => NotificationActivator.Revoke(_activator);

    private static void Show(Toast toast)
    {
        var xml = new XmlDocument();
        xml.LoadXml(toast.ToString());
        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
    }

    // Written each time the app starts, so it follows the app when it moves, and a click starts it with this home
    private static void Register()
    {
        using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            app.SetValue("DisplayName", Program.AppName);
            app.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            app.SetValue("CustomActivator", ActivatorId.ToString("B"));
        }

        using var server = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{ActivatorId:B}\LocalServer32");
        server.SetValue("", $"\"{Environment.ProcessPath}\" --home \"{Program.Home}\"");
    }

    private void OnActivated(string arguments)
    {
        switch (arguments)
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
