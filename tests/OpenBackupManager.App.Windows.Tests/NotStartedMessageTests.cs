namespace OpenBackupManager.App.Windows.Tests;

public class NotStartedMessageTests
{
    private const string Folder = @"E:\OBM";

    [Test]
    public void HandedOver_SaysNothing() => Assert.That(Program.NotStartedMessage(handedOver: true, atLogin: false, error: null, Folder), Is.Null);

    // Nobody launched it, so there's nobody to tell
    [Test]
    public void AtSignIn_WithAnotherCopyRunning_SaysNothing() =>
        Assert.That(Program.NotStartedMessage(handedOver: false, atLogin: true, error: null, Folder), Is.Null);

    [Test]
    public void WithAnotherCopyRunning_SaysSo() =>
        Assert.That(Program.NotStartedMessage(handedOver: false, atLogin: false, error: null, Folder), Does.Contain("already running"));

    // Rather than blaming another copy, which the user would look for and not find
    [TestCase(false)]
    [TestCase(true)]
    public void WithAFolderItCantUse_SaysWhyAndWhere(bool atLogin)
    {
        var message = Program.NotStartedMessage(handedOver: false, atLogin, new IOException("The disk is full."), Folder);

        Assert.That(message, Does.Contain("can't use its folder").And.Contain("The disk is full.").And.Contain(Folder).And.Not.Contain("already running"));
    }
}
