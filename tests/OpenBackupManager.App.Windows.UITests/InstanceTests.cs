using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using OpenBackupManager.Core;

namespace OpenBackupManager.App.Windows.UITests;

// A copy in another session, or another exe given the same home, holds the app's lock where AppInstance can't find it,
// which the test stands in for by taking the lock itself
[Explicit("Takes over the desktop")]
[Category("Sandbox")]
public sealed class InstanceTests : AppTests
{
    [Test]
    public void ACopyRunningElsewhere_IsExplained()
    {
        Quit();
        using var elsewhere = ProcessLock.TryTake("app", AppPaths.InHome(Home));

        StartAgain();

        var message = WaitForWindow(AppName);
        Assert.That(message.FindFirstDescendant(c => c.ByControlType(ControlType.Text))?.Name, Does.Contain("already running in another session"));
        message.FindFirstDescendant(c => c.ByControlType(ControlType.Button).And(c.ByName("OK")))!.AsButton().Invoke();
        Assert.That(WaitForExit(), Is.True);
    }

    [Test]
    public void ACopyRunningElsewhere_AtLogin_ExitsQuietly()
    {
        Quit();
        using var elsewhere = ProcessLock.TryTake("app", AppPaths.InHome(Home));

        StartAgain("--at-login");

        Assert.That(WaitForExit(), Is.True);
    }
}
