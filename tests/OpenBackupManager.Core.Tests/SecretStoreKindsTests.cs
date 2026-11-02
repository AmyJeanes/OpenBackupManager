using System.Runtime.InteropServices;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Core.Tests;

public class SecretStoreKindsTests
{
    [Test]
    public void TheDefault_IsTheOSsOwnStore_OrNoneOnLinux()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SecretStoreKinds.DefaultOn(OSPlatform.Windows), Is.EqualTo(SecretStoreKind.CredentialManager));
            Assert.That(SecretStoreKinds.DefaultOn(OSPlatform.OSX), Is.EqualTo(SecretStoreKind.Keychain));
            Assert.That(SecretStoreKinds.DefaultOn(OSPlatform.Linux), Is.Null);
        }
    }

    [TestCase("Windows", new[] { "credential-manager", "file" })]
    [TestCase("OSX", new[] { "keychain", "file" })]
    [TestCase("Linux", new[] { "secret-service", "file" })]
    public void EachOS_HasItsOwnStore_AndTheFile(string os, string[] names) =>
        Assert.That(SecretStoreKinds.NamesOn(OSPlatform.Create(os)), Is.EqualTo(names));

    [Test]
    public void EveryStoreHasANameThatParsesBack() =>
        Assert.That(Enum.GetValues<SecretStoreKind>().Select(k => SecretStoreKinds.Parse(k.Name())), Is.EqualTo(Enum.GetValues<SecretStoreKind>().Cast<SecretStoreKind?>()));

    [Test]
    public void TheSettingsChoices_AreThisOSsStores() =>
        Assert.That(Settings.Find("secret-store")!.Choices, Is.EqualTo(SecretStoreKinds.NamesOn(CurrentOS.Platform)));
}
