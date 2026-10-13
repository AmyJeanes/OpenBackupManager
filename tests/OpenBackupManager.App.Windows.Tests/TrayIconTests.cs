using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows.Tests;

public class TrayIconTests
{
    [Test]
    public void EveryStateHasLightAndDarkIcons([Values] SyncState state, [Values] bool light) => Assert.That(File.Exists(Tray.IconPath(state, light)), Is.True);
}
