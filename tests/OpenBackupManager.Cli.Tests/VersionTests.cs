using System.Reflection;

namespace OpenBackupManager.Cli.Tests;

public class VersionTests
{
    [TestCase("obm")]
    [TestCase("obmd")]
    public void Version_IsTheBuildsVersion(string tool)
    {
        var (exitCode, output) = Tool.Run(tool, "--version");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output.Trim(), Is.EqualTo(typeof(Status).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion));
        }
    }
}
