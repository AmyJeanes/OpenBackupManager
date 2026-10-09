namespace OpenBackupManager.App.Windows.Tests;

public sealed class HostTests
{
    // Building checks each service's dependencies without creating any, since they need the UI thread
    [Test]
    public void Build_FindsEveryServicesDependencies() => Assert.DoesNotThrow(() => Program.BuildHost().Dispose());
}
