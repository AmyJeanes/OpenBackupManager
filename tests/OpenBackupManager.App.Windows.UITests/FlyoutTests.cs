using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace OpenBackupManager.App.Windows.UITests;

public sealed class FlyoutTests : AppTests
{
    [Test]
    public void OpensAtLaunch_AndEscClosesIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LaunchFlyout.FindFirstDescendant(c => c.ByAutomationId("Caption"))?.Name, Is.EqualTo(AppName));
            Assert.That(LaunchFlyout.FindFirstDescendant(c => c.ByAutomationId("StatusText"))?.Name, Is.Not.Empty);
        });

        CloseWithEsc(LaunchFlyout);
    }

    [Test]
    public void LaunchingAgain_OpensItInTheRunningCopy()
    {
        CloseWithEsc(LaunchFlyout);

        using var second = LaunchApp();

        Assert.Multiple(() =>
        {
            Assert.That(second.WaitForExit(Timeout), Is.True);
            Assert.That(second.ExitCode, Is.Zero);
        });
        WaitForFlyout();
    }

    [Test]
    public void Open_OpensTheMainWindow()
    {
        OpenButton(LaunchFlyout).Invoke();

        WaitForWindow(AppName);
        WaitUntilHidden(LaunchFlyout);
    }

    [Test]
    public void ClickingElsewhere_ClosesIt()
    {
        OpenButton(LaunchFlyout).Invoke();
        var window = WaitForWindow(AppName);
        WaitUntilHidden(LaunchFlyout);
        using (LaunchApp())
        {
            WaitForFlyout();
        }

        Mouse.Click(window.BoundingRectangle.Center());

        WaitUntilHidden(LaunchFlyout);
    }

    private static Button OpenButton(Window flyout) => flyout.FindFirstDescendant(c => c.ByAutomationId("OpenButton"))!.AsButton();
}
