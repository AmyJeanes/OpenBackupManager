using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenBackupManager.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace OpenBackupManager.Platform.Windows.Tests;

[Explicit("Uses the real Credential Manager")]
[Category("Sandbox")]
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
    public void Save_PutsItInCredentialManager_AsUtf16_KeptToThisComputer()
    {
        Save(Store(), "rclone-config", "pässwörd 🔑");

        var credential = InCredentialManager("rclone-config");

        Assert.That(credential, Is.EqualTo(("rclone-config", Encoding.Unicode.GetBytes("pässwörd 🔑"), CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE)));
    }

    [Test]
    public void Read_ACredentialCmdkeySaved()
    {
        var store = Store();
        _saved.Add((store, "rclone-config"));

        Cmdkey($"/generic:{_prefix}/rclone-config", "/user:rclone-config", "/pass:pässwörd");

        Assert.That(store.Read("rclone-config")?.Reveal(), Is.EqualTo("pässwörd"));
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
    public void Remove_TakesItOutOfCredentialManager()
    {
        var store = Store();
        Save(store, "rclone-config", "secret");

        store.Remove("rclone-config");

        Assert.That(InCredentialManager("rclone-config"), Is.Null);
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

    // Read with Windows' own call, so a mistake the store makes both saving and reading still shows
    private unsafe (string UserName, byte[] Value, CRED_PERSIST Persist)? InCredentialManager(string name)
    {
        if (!PInvoke.CredRead($"{_prefix}/{name}", CRED_TYPE.CRED_TYPE_GENERIC, out var credential))
        {
            Assert.That(Marshal.GetLastPInvokeError(), Is.EqualTo((int)WIN32_ERROR.ERROR_NOT_FOUND), "CredRead");
            return null;
        }

        try
        {
            return (credential->UserName.ToString(), new ReadOnlySpan<byte>(credential->CredentialBlob, (int)credential->CredentialBlobSize).ToArray(), credential->Persist);
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    private static void Cmdkey(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("cmdkey", arguments) { RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.Zero, "cmdkey");
    }

    private void Save(CredentialManager store, string name, string value)
    {
        _saved.Add((store, name));
        store.Save(name, new Secret(value));
    }
}
