using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Core.Tests;

public class RunFolderTests
{
    private static readonly AppPaths Home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm"));

    [Test]
    public void Socket_IsInTheRunFolder() =>
        Assert.That(RunFolder.Socket(Home, "engine.sock"), Is.EqualTo(Path.Combine(Home.State, "run", "engine.sock")));

    // Longer than the engine checks it has room for
    [Test]
    public void Socket_WithALongerName_IsRefused() =>
        Assert.That(() => RunFolder.Socket(Home, new string('x', 17)), Throws.TypeOf<ArgumentOutOfRangeException>());
}
