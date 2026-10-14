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
}
