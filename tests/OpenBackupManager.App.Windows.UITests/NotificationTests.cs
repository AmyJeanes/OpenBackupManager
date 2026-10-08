using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace OpenBackupManager.App.Windows.UITests;

// Notifications are found in the notification centre rather than as they pop up as CI has Do Not Disturb enabled
public sealed class NotificationTests : AppTests
{
    private const string TestTitle = "Sync failed";

    [TearDown]
    public void ClearNotifications()
    {
        OpenNotificationCentre().FindFirstDescendant(c => c.ByName($"Clear all notifications for {AppName}"))?.AsButton().Invoke();
        Keyboard.Type(VirtualKeyShort.ESCAPE);
    }

    [Test]
    public void ViewDetails_OpensTheMainWindow()
    {
        ChooseFromTrayMenu("Show test notification");

        var notification = FindNotification(TestTitle);
        // Its buttons show once it's expanded
        notification.FindFirstChild(c => c.ByAutomationId("ExpandButton"))!.AsButton().Invoke();
        Retry.WhileNull(() => notification.FindFirstChild(c => c.ByControlType(ControlType.Button).And(c.ByName("View details"))), Timeout, throwOnTimeout: true)
            .Result!.AsButton().Invoke();

        WaitForWindow(AppName);
    }

    [Test]
    public void ClickingAfterQuit_StartsTheAppWithTheMainWindow()
    {
        ChooseFromTrayMenu("Show test notification");
        Quit();

        var clicked = DateTime.Now;
        FindNotification(TestTitle).Click();

        WaitForCopyStartedSince(clicked);
        WaitForWindow(AppName);
        Assert.That(IsFlyoutShown(), Is.False);
    }

    private AutomationElement FindNotification(string title)
    {
        var centre = OpenNotificationCentre();
        return Retry.WhileNull(
                () => centre.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))
                    .FirstOrDefault(n => n.FindFirstChild(c => c.ByAutomationId("Title"))?.Name == title),
                Timeout,
                throwOnTimeout: true,
                ignoreException: true)
            .Result!;
    }

    private AutomationElement OpenNotificationCentre()
    {
        if (NotificationCentre() is null)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.KEY_N);
        }

        return Retry.WhileNull(NotificationCentre, Timeout, throwOnTimeout: true, ignoreException: true).Result!;
    }

    private AutomationElement? NotificationCentre() =>
        Desktop.FindAllChildren(c => c.ByClassName("Windows.UI.Core.CoreWindow"))
            .Select(w => w.FindFirstChild(c => c.ByAutomationId("NotificationCenterGrid")))
            .FirstOrDefault(c => c is not null);
}
