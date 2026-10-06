namespace OpenBackupManager.App.Windows.Tests;

public class TrayIconTests
{
    [Test]
    public void EveryStateHasLightAndDarkIcons([Values] TrayState state, [Values] bool light) => Assert.That(File.Exists(Tray.IconPath(state, light)), Is.True);
}
