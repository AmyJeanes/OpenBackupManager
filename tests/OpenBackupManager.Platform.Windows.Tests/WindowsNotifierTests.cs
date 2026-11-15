using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.Windows.Tests;

public class WindowsNotifierTests
{
    [Test]
    public void Toast_HasTheNotificationsTextAndButtons()
    {
        var notification = new Notification("Update available", ["Version 1.2.3 is ready to install"], "open")
        {
            Buttons =
            [
                new ActionButton("Restart now", "restart"),
                new LinkButton("What's new", new Uri("https://example.com/releases/tag/v1.2.3")),
                new DismissButton("Not now"),
            ],
        };

        var expected = new Toast("open")
            .Text("Update available")
            .Text("Version 1.2.3 is ready to install")
            .Button("Restart now", "restart")
            .Link("What's new", new Uri("https://example.com/releases/tag/v1.2.3"))
            .DismissButton("Not now");
        Assert.That(WindowsNotifier.Toast(notification).ToString(), Is.EqualTo(expected.ToString()));
    }

    // As when Windows' notification services are turned off, which closed the app at every start
    [Test]
    public void WithoutTheNotificationPlatform_ItStillStarts_AndShowsNothing()
    {
        try
        {
            Assert.That(
                () =>
                {
                    using var notifier = new WindowsNotifier(
                        "Test",
                        "icon.ico",
                        "",
                        NullLogger.Instance,
                        // WPN_E_PLATFORM_UNAVAILABLE, as Windows throws it
                        () => throw Marshal.GetExceptionForHR(unchecked((int)0x803E0105))!);
                    notifier.Show(new Notification("Update available", ["Version 1.2.3 is ready to install"]));
                },
                Throws.Nothing);
        }
        finally
        {
            WindowsNotifier.Unregister();
        }
    }
}
