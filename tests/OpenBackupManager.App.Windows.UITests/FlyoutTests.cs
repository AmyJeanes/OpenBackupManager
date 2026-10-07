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
}
