using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace OpenBackupManager.App.Windows.UITests;

public sealed class NotificationTests : AppTests
{
    private const string TestTitle = "Sync failed";

    [TearDown]
    public void ClearAfterTest() => ClearNotifications();

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
}
