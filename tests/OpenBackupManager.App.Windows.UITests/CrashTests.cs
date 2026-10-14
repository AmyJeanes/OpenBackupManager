using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;
using Windows.Win32.System.Recovery;

namespace OpenBackupManager.App.Windows.UITests;

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
        var crash = WaitForWindow(AppName + " closed unexpectedly");
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
        WaitForWindow(AppName + " closed unexpectedly");
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
            .And.Not.Contain("ended without quitting"));
    }

    private string Log()
    {
        var file = Directory.GetFiles(Path.Combine(Home, "logs"), "app-*.log").Single();
        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

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
