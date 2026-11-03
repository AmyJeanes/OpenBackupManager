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
}
