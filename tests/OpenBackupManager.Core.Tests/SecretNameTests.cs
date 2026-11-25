using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Core.Tests;

public class SecretNameTests
{
    [TestCase("rclone-config")]
    [TestCase("a")]
    [TestCase("0")]
    [TestCase("webhook-b7e1c2qz")]
    [TestCase("a123456789012345678901234567890123456789012345678901234567890123")]
    public void Check_TakesAName(string name) => Assert.That(() => SecretName.Check(name), Throws.Nothing);

    [TestCase("")]
    [TestCase("Rclone-Config")]
    [TestCase("-rclone")]
    [TestCase("rclone\n")]
    [TestCase("rclone config")]
    [TestCase("rclone/config")]
    [TestCase("rclone\"config")]
    [TestCase("pässwörd")]
    [TestCase("a1234567890123456789012345678901234567890123456789012345678901234")]
    public void Check_RefusesAnythingElse(string name) =>
        Assert.That(() => SecretName.Check(name), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("name"));

    [TestCase("rclone-config", "OBM_RCLONE_CONFIG_SECRET")]
    [TestCase("webhook-b7e1c2qz", "OBM_WEBHOOK_B7E1C2QZ_SECRET")]
    public void EnvVar_IsItsNameAsAVariable(string name, string expected) => Assert.That(SecretName.EnvVar(name), Is.EqualTo(expected));
}
