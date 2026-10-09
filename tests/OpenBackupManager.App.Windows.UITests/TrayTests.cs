using FlaUI.Core.Input;
using Windows.Win32;

namespace OpenBackupManager.App.Windows.UITests;

public sealed class TrayTests : AppTests
{
    [Test]
    public void Click_OpensTheFlyout_AndClickingAgainClosesIt()
    {
        CloseWithEsc(LaunchFlyout);
        PauseBetweenClicks();

        Mouse.Click(TrayIcon());
        var flyout = WaitForFlyout();
        PauseBetweenClicks();
        Mouse.Click(TrayIcon());

        WaitUntilHidden(flyout);
    }

    [Test]
    public void DoubleClick_OpensTheMainWindow()
    {
        CloseWithEsc(LaunchFlyout);

        Mouse.DoubleClick(TrayIcon());

        WaitForWindow(AppName);
        Assert.That(IsFlyoutShown(), Is.False);
    }

    [Test]
    public void Open_OpensTheMainWindow()
    {
        ChooseFromTrayMenu("Open");

        WaitForWindow(AppName);
    }

    [Test]
    public void About_OpensAbout()
    {
        ChooseFromTrayMenu("About");

        var about = WaitForWindow("About " + AppName);
        Assert.That(about.FindFirstDescendant(c => c.ByAutomationId("NameText"))?.Name, Is.EqualTo(AppName));
    }

    [Test]
    public void Quit_ClosesTheApp() => Quit();

    // As long as a person would. Windows takes a quicker second click as a double-click, and the flyout ignores a click
    // just after it closes
    private static void PauseBetweenClicks() => Thread.Sleep((int)PInvoke.GetDoubleClickTime());
}
