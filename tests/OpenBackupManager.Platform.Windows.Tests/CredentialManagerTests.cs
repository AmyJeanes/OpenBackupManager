using OpenBackupManager.Core;

namespace OpenBackupManager.Platform.Windows.Tests;

public class CredentialManagerTests
{
    private readonly List<(CredentialManager Store, string Name)> _saved = [];
    private string _prefix = "";

    [SetUp]
    public void SetUp() => _prefix = "OpenBackupManager-Tests-" + Guid.NewGuid().ToString("N");

    [TearDown]
    public void RemoveSaved()
    {
        foreach (var (store, name) in _saved)
        {
            store.Remove(name);
        }
    }

    [Test]
    public void Read_GivesWhatWasSaved()
    {
        var store = Store();

        Save(store, "rclone-config", "pässwörd 🔑");

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo("pässwörd 🔑"));
    }

    [Test]
    public void Read_IsNullWhenNothingWasSaved() => Assert.That(Store().Read("rclone-config"), Is.Null);

    [Test]
    public void Save_ReplacesTheSecret()
    {
        var store = Store();

        Save(store, "rclone-config", "first");
        Save(store, "rclone-config", "second");

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo("second"));
    }

    [Test]
    public void Save_TakesTheLongestSecretCredentialManagerHolds()
    {
        var store = Store();
        var longest = new string('x', CredentialManager.MaxLength);

        Save(store, "rclone-config", longest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo(longest));
            Assert.That(() => store.Save("rclone-config", new Secret(longest + "x")), Throws.ArgumentException);
        }
    }

    [Test]
    public void Remove_RemovesTheSecret()
    {
        var store = Store();
        Save(store, "rclone-config", "secret");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Remove("rclone-config"), Is.True);
            Assert.That(store.Read("rclone-config"), Is.Null);
            Assert.That(store.Remove("rclone-config"), Is.False);
        }
    }

    [Test]
    public void Stores_WithDifferentPrefixesKeepTheirOwnSecrets()
    {
        var store = Store();
        var other = new CredentialManager(_prefix + "-other");

        Save(store, "rclone-config", "this home's");
        Save(other, "rclone-config", "another home's");

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo("this home's"));
    }

    private CredentialManager Store() => new(_prefix);

    private void Save(CredentialManager store, string name, string value)
    {
        _saved.Add((store, name));
        store.Save(name, new Secret(value));
    }
}
