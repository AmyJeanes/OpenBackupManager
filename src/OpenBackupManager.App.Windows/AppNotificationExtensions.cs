using System.Xml.Linq;
using Microsoft.Windows.AppNotifications;

namespace OpenBackupManager.App.Windows;

// Windows' notifications support buttons that Windows handles itself, but the Windows App SDK's builder can only make
// buttons that call back into the app. Replace once https://github.com/microsoft/WindowsAppSDK/issues/2960 is released
internal static class AppNotificationExtensions
{
    // A button that closes the notification without starting the app, which a builder button would if it had exited
    public static AppNotification WithDismissButton(this AppNotification notification, string content) =>
        new(WithDismissButton(notification.Payload, content));

    public static string WithDismissButton(string payload, string content)
    {
        var toast = XDocument.Parse(payload).Root!;
        var actions = toast.Element("actions");
        if (actions is null)
        {
            actions = new XElement("actions");
            toast.Add(actions);
        }

        actions.Add(new XElement(
            "action",
            new XAttribute("content", content),
            new XAttribute("arguments", "dismiss"),
            new XAttribute("activationType", "system")));
        return toast.ToString(SaveOptions.DisableFormatting);
    }
}
