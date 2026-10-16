namespace OpenBackupManager.Core.Tests;

// Within one process. The engine's tests check it between processes, and when one dies
public class ProcessLockTests
{
    private AppPaths _home = null!;

    [SetUp]
    public void SetUp() => _home = Home();

    [TearDown]
    public void TearDown() => Directory.Delete(_home.State, recursive: true);

    [Test]
    public void WhileHeld_CantBeTakenAgain()
    {
        using var held = ProcessLock.TryTake("engine", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(held, Is.Not.Null);
            Assert.That(TakeOnAnotherThread("engine", _home), Is.False);
        }
    }

    [Test]
    public void OnceReleased_CanBeTakenAgain()
    {
        ProcessLock.TryTake("engine", _home)!.Dispose();

        Assert.That(TakeOnAnotherThread("engine", _home), Is.True);
    }

    [Test]
    public void EachName_IsItsOwnLock()
    {
        using var held = ProcessLock.TryTake("engine", _home);

        Assert.That(TakeOnAnotherThread("app", _home), Is.True);
    }

    [Test]
    public void EachHome_HasItsOwnLock()
    {
        var other = Home();
        try
        {
            using var held = ProcessLock.TryTake("engine", _home);

            Assert.That(TakeOnAnotherThread("engine", other), Is.True);
        }
        finally
        {
            Directory.Delete(other.State, recursive: true);
        }
    }

    private static AppPaths Home()
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-lock-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(home);
        return AppPaths.InHome(home);
    }

    // A thread that holds a mutex can take it again, so trying from the test's own thread would always succeed on Windows
    private static bool TakeOnAnotherThread(string name, AppPaths paths)
    {
        var taken = false;
        var thread = new Thread(() =>
        {
            using var taking = ProcessLock.TryTake(name, paths);
            taken = taking is not null;
        });
        thread.Start();
        thread.Join();
        return taken;
    }
}
