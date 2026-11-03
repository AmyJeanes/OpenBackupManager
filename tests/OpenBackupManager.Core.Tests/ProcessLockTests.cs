using System.Diagnostics;

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
        using var again = ProcessLock.TryTake("engine", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(held, Is.Not.Null);
            Assert.That(again, Is.Null);
        }
    }

    [Test]
    public void OnceReleased_CanBeTakenAgain()
    {
        ProcessLock.TryTake("engine", _home)!.Dispose();

        using var again = ProcessLock.TryTake("engine", _home);

        Assert.That(again, Is.Not.Null);
    }

    [Test]
    public void EachName_IsItsOwnLock()
    {
        using var held = ProcessLock.TryTake("engine", _home);
        using var other = ProcessLock.TryTake("app", _home);

        Assert.That(other, Is.Not.Null);
    }

    [Test]
    public void EachHome_HasItsOwnLock()
    {
        var otherHome = Home();
        try
        {
            using var held = ProcessLock.TryTake("engine", _home);
            using var other = ProcessLock.TryTake("engine", otherHome);

            Assert.That(other, Is.Not.Null);
        }
        finally
        {
            Directory.Delete(otherHome.State, recursive: true);
        }
    }

    [Test]
    public void ThroughAnotherPathToTheHome_CantBeTakenAgain()
    {
        var link = _home.State + "-link";
        LinkFolder(link, _home.State);
        try
        {
            using var held = ProcessLock.TryTake("engine", _home);
            using var again = ProcessLock.TryTake("engine", AppPaths.InHome(link));

            Assert.That(again, Is.Null);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    // As a backup program or antivirus might while the engine starts. Only Windows checks this for every program: an flock
    // only meets other flocks, and .NET's reader here would take one
    [Test]
    [Platform("Win")]
    public void WhileItsFileIsBeingRead_CanBeTaken()
    {
        ProcessLock.TryTake("engine", _home)!.Dispose();
        using var reader = new FileStream(Path.Combine(_home.State, "engine.lock"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        using var held = ProcessLock.TryTake("engine", _home);

        Assert.That(held, Is.Not.Null);
    }

    private static AppPaths Home()
    {
        var home = Path.Combine(Path.GetTempPath(), "obm-lock-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(home);
        return AppPaths.InHome(home);
    }

    // A symbolic link needs admin or Developer Mode on Windows, where a junction doesn't. .NET can't make a junction
    private static void LinkFolder(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target]) { RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.That(mklink.ExitCode, Is.Zero, "mklink /J failed");
    }
}
