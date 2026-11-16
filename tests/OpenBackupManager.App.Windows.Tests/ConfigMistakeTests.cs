using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.App.Windows.Tests;

public class ConfigMistakeTests
{
    private const string ConfigFile = @"C:\Users\Someone\AppData\Local\OpenBackupManager\config.toml";

    [Test]
    public void InTheFile_SaysToFixTheFile() =>
        Assert.That(
            Program.ConfigMistake(new ConfigException("", new ConfigProblem(3, ConfigProblemKind.ShouldBeTrueOrFalse, true, "updates.automatic")), ConfigFile),
            Does.Contain("won't start until its settings file is fixed").And.Contain("updates.automatic").And.Contain(ConfigFile));

    // The variable wins over the file, so fixing the file wouldn't help
    [Test]
    public void InAnEnvironmentVariable_SaysToFixTheVariable_NotTheFile()
    {
        var problem = new ConfigProblem(0, ConfigProblemKind.ShouldBeTrueOrFalse, true, "updates.automatic", "OBM_UPDATES_AUTOMATIC");

        var message = Program.ConfigMistake(new ConfigException("", problem), ConfigFile);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("won't start until the OBM_UPDATES_AUTOMATIC environment variable is fixed or removed"));
            Assert.That(message, Does.Contain("Environment variable OBM_UPDATES_AUTOMATIC: updates.automatic should be true or false"));
            Assert.That(message, Does.Not.Contain("settings file").And.Not.Contain(ConfigFile));
        }
    }
}
