using System.Reflection;

namespace OpenBackupManager.Cli.Tests;

public class VersionTests
{
    [Test]
    public void Version_IsTheBuildsVersion()
    {
        var (exitCode, output) = Obm.Run("--version");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output.Trim(), Is.EqualTo(typeof(Daemon).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion));
        }
    }
}
