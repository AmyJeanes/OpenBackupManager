using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.ViewModels.Tests;

[SetUICulture("en-GB")]
public class QuitViewModelTests
{
    private static readonly AppInfo App = new("Open Backup Manager", "1.2.3", null, new Uri("https://example.com/obm"));

    [Test]
    public void AsksAboutTheApp() => Assert.That(new QuitViewModel(App, null).Title, Is.EqualTo("Quit Open Backup Manager?"));

    [TestCase(EngineStarter.App, "Any backup or sync in progress stops until you open the app again.")]
    [TestCase(EngineStarter.Service, "Backups and syncing carry on without the app.")]
    [TestCase(EngineStarter.Other, "Backups and syncing carry on without the app.")]
    [TestCase(null, "")]
    public void SaysWhatHappensToTheEngine(EngineStarter? engine, string detail) =>
        Assert.That(new QuitViewModel(App, engine).Detail, Is.EqualTo(detail));
}
