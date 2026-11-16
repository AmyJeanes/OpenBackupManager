using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Tools;
using Windows.Win32.System.Recovery;

namespace OpenBackupManager.App.Windows.UITests;

[Explicit("Takes over the desktop")]
[Category("Sandbox")]
public sealed class CrashTests : AppTests
{
    // A crash that fails fast runs no handler, so its report comes from what Windows recorded
    [TestCase("On the UI thread", "System.InvalidOperationException: Test crash on the UI thread")]
    [TestCase("On another thread", "System.InvalidOperationException: Test crash on another thread")]
    [TestCase("Fail fast", "Message: Test crash that fails fast")]
    public void ACrash_IsReportedAtTheNextStartOnly(string item, string error)
    {
        Crash(item);

        StartAgain();
        var crash = WaitForCrashWindow(AppName + " closed unexpectedly");
        var report = crash.FindFirstDescendant(c => c.ByAutomationId("ReportText"))!.AsTextBox().Text;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report, Does.StartWith(AppName + " closed unexpectedly at").And.Contain(error));
            // The crashed run's log, from its first line
            Assert.That(report, Does.Contain("App: Started version"));
            Assert.That(IsFlyoutShown(), Is.False);
        }

        crash.Close();
        Quit();
        // Opens the flyout, which it only does when there's no new crash to show
        LaunchAgain();
    }

    // Clipboard managers and remote desktop have the clipboard open for moments, which can be as Copy report is clicked
    [Test]
    public void CopyReport_WhileAnotherAppHasTheClipboard_SaysSo_AndCopiesOnceItsFree()
    {
        Crash("On the UI thread");
        StartAgain();
        var crash = WaitForCrashWindow(AppName + " closed unexpectedly");
        var copy = crash.FindFirstDescendant(c => c.ByAutomationId("CopyButton"))!.AsButton();

        NativeMethods.HoldClipboard();
        try
        {
            // An error in the click comes back to the test, failing it, where a real click would close the app
            copy.Invoke();
            Assert.That(Retry.WhileNull(() => CantCopy(crash), Timeout).Result, Is.Not.Null, "It didn't say it couldn't copy");
        }
        finally
        {
            NativeMethods.LetGoOfClipboard();
        }

        copy.Invoke();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Retry.WhileFalse(() => CantCopy(crash) is null, Timeout).Result, Is.True, "It still says it couldn't copy");
            Assert.That(App.HasExited, Is.False);
        }

        crash.Close();
    }

    // Windows only restarts a copy that has run for 60 seconds, so this takes over a minute
    [Test]
    [Explicit("Takes over a minute")]
    public void Windows_RestartsTheAppAfterACrash()
    {
        var running = DateTime.Now - App.StartTime;
        if (running < TimeSpan.FromSeconds(61))
        {
            Thread.Sleep(TimeSpan.FromSeconds(61) - running);
        }

        var crashed = DateTime.Now;
        Crash("On another thread");

        WaitForCopyStartedSince(crashed);
        WaitForCrashWindow(AppName + " closed unexpectedly");
    }

    [Test]
    public void Windows_RestartsTheAppWithItsHome()
    {
        var settings = NativeMethods.RestartSettings(App);

        Assert.That(settings, Is.EqualTo((
            $"--home \"{Home}\"",
            REGISTER_APPLICATION_RESTART_FLAGS.RESTART_NO_PATCH | REGISTER_APPLICATION_RESTART_FLAGS.RESTART_NO_REBOOT)));
    }

    [Test]
    public void SigningOut_QuitsWithoutLookingLikeACrash()
    {
        NativeMethods.SignOut(LaunchFlyout);
        Assert.That(WaitForExit(), Is.True);

        LaunchAgain();
        Assert.That(Log(), Does.Contain("Quitting, as Windows is signing out or shutting down")
            .And.Contain("Stopping the app's services")
            .And.Not.Contain("The last run closed unexpectedly"));
    }

    // Only there while it's shown, as UI Automation leaves out what's collapsed
    private static AutomationElement? CantCopy(Window crash) => crash.FindFirstDescendant(c => c.ByAutomationId("CantCopyText"));

    private void Crash(string item)
    {
        try
        {
            ChooseFromTrayMenu("Crash", item);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ElementNotAvailableException)
        {
            // UI Automation reports a failure when the app crashes before the invoke returns
        }

        Assert.That(WaitForExit(), Is.True);
    }
}
