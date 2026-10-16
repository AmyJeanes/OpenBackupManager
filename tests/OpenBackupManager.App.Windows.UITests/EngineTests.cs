using System.Diagnostics;
using FlaUI.Core.Tools;

namespace OpenBackupManager.App.Windows.UITests;

public sealed class EngineTests : AppTests
{
    [Test]
    public void TheAppStartsTheEngine_AndSaysOnceItsConnected()
    {
        WaitUntilConnected();

        Assert.That(EngineStatus()?.StartedByApp, Is.True);
    }

    [Test]
    public void Quit_StopsTheEngineTheAppStarted()
    {
        using var engine = Engine();

        Quit();

        Assert.That(engine.WaitForExit(Timeout), Is.True);
    }

    // The engine keeps running when the app doesn't quit, and says the app started it, so the next copy knows
    [Test]
    public void AfterACrash_TheNextCopyConnects_AndQuitStillStopsTheEngine()
    {
        using var engine = Engine();
        EndApp();

        LaunchAgain();
        WaitUntilConnected();
        Assert.That(EngineStatus()?.ProcessId, Is.EqualTo(engine.Id));
        Quit();

        Assert.That(engine.WaitForExit(Timeout), Is.True);
    }

    [Test]
    public void Quit_LeavesAnEngineStartedAnotherWayRunning()
    {
        using (var started = Engine())
        {
            Quit();
            // Until it has, another can't start
            Assert.That(started.WaitForExit(Timeout), Is.True);
        }

        using var engine = StartEngineByHand();
        Assert.That(Retry.WhileFalse(() => EngineStatus()?.ProcessId == engine.Id, Timeout, ignoreException: true).Result, Is.True);

        LaunchAgain();
        WaitUntilConnected();
        Assert.That(EngineStatus()?.ProcessId, Is.EqualTo(engine.Id));
        Quit();

        Assert.That(engine.WaitForExit(TimeSpan.FromSeconds(2)), Is.False);
    }

    [Test]
    public void AnEngineThatEnds_IsStartedAgain()
    {
        using var first = Engine();

        first.Kill();
        first.WaitForExit();

        Assert.That(Retry.WhileFalse(() => EngineStatus() is { } status && status.ProcessId != first.Id, Timeout, ignoreException: true).Result, Is.True);
        WaitUntilConnected();
    }

    // The engine the app started, once it's connected
    private Process Engine()
    {
        WaitUntilConnected();
        return Process.GetProcessById(EngineStatus()!.ProcessId);
    }
}
