using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using OpenBackupManager.Core.Platform;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Notification = OpenBackupManager.Core.Platform.Notification;

namespace OpenBackupManager.Platform.Windows;

// Notifications through Windows' own toast API. Their buttons come back to this copy of the app, or start it if it
// isn't running
public sealed class WindowsNotifier : INotifier, IDisposable
{
    // Windows groups notifications by the app's ID. Velopack gives the installed app its Start menu shortcut's ID,
    // and a copy that wasn't installed gets one from its path, so copies in different folders keep their own
    private static readonly string AppId = NativeMethods.ExplicitAppId()
        ?? "OpenBackupManager.Dev." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.ProcessPath!.ToUpperInvariant())))[..16];

    // The activator's COM class, made from the app's ID so each copy keeps the same one
    private static readonly Guid ActivatorId = new(SHA256.HashData(Encoding.UTF8.GetBytes(AppId))[..16]);

    // Clicks arrive on a COM thread, and are passed on through this
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly uint _activator;
    private readonly ToastNotifier _notifier;

    // launchArguments are what Windows starts the app with to deliver a click when it isn't running
    public WindowsNotifier(string appName, string iconPath, string launchArguments)
    {
        Register(appName, iconPath, launchArguments);
        _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
        _activator = NotificationActivator.Register(ActivatorId, OnActivated);
    }

    public event Action<string>? Clicked;

    // Windows starts the app this way to deliver a click when it isn't running, and the click then reaches the activator
    public static bool StartedByClick => Environment.GetCommandLineArgs().Contains("-Embedding");

    public void Show(Notification notification)
    {
        var xml = new XmlDocument();
        xml.LoadXml(Toast(notification).ToString());
        _notifier.Show(new ToastNotification(xml));
    }

    // When uninstalling, so nothing is left behind
    public static void Unregister()
    {
        ToastNotificationManager.History.Clear(AppId);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{AppId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{ActivatorId:B}", throwOnMissingSubKey: false);
    }

    // Clicks after this start the app again
    public void Dispose() => NotificationActivator.Revoke(_activator);

    internal static Toast Toast(Notification notification)
    {
        var toast = new Toast(notification.Action).Text(notification.Title);
        foreach (var line in notification.Lines)
        {
            toast.Text(line);
        }

        foreach (var button in notification.Buttons)
        {
            _ = button switch
            {
                ActionButton action => toast.Button(action.Label, action.Action),
                LinkButton link => toast.Link(link.Label, link.Link),
                DismissButton dismiss => toast.DismissButton(dismiss.Label),
                _ => throw new ArgumentException($"Unknown button {button}", nameof(notification)),
            };
        }

        return toast;
    }

    // Written each time the app starts, so it follows the app when it moves, and a click starts it with the same arguments
    private static void Register(string appName, string iconPath, string launchArguments)
    {
        using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            app.SetValue("DisplayName", appName);
            app.SetValue("IconUri", iconPath);
            app.SetValue("CustomActivator", ActivatorId.ToString("B"));
        }

        using var server = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{ActivatorId:B}\LocalServer32");
        server.SetValue("", $"\"{Environment.ProcessPath}\" {launchArguments}");
    }

    // Posted, rather than raised straight away, so a click that starts the app waits until the app has added its handlers
    private void OnActivated(string arguments)
    {
        if (_context is null)
        {
            Clicked?.Invoke(arguments);
        }
        else
        {
            _context.Post(_ => Clicked?.Invoke(arguments), null);
        }
    }
}
