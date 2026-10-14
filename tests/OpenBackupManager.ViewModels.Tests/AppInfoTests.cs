namespace OpenBackupManager.ViewModels.Tests;

public class AppInfoTests
{
    [Test]
    public void FromAssembly_LeavesBuildDetailsOutOfTheVersion()
    {
        var app = AppInfo.FromAssembly("Open Backup Manager", typeof(AppInfoTests).Assembly);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.Name, Is.EqualTo("Open Backup Manager"));
            Assert.That(app.Version, Is.Not.Empty.And.Not.Contain('+'));
            Assert.That(app.Copyright, Does.StartWith("©"));
            Assert.That(app.Repository, Is.EqualTo(new Uri("https://github.com/AmyJeanes/OpenBackupManager")));
        }
    }
}
