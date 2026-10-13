namespace OpenBackupManager.ViewModels.Tests;

public class AppInfoTests
{
    [Test]
    public void FromAssembly_LeavesBuildDetailsOutOfTheVersion()
    {
        var app = AppInfo.FromAssembly("OpenBackupManager", typeof(AppInfoTests).Assembly);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.Name, Is.EqualTo("OpenBackupManager"));
            Assert.That(app.Version, Is.Not.Empty.And.Not.Contain('+'));
            Assert.That(app.Copyright, Does.StartWith("©"));
        }
    }
}
