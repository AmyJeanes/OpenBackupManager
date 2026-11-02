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
    public async Task RemoveSaved()
    {
        foreach (var (store, name) in _saved)
        {
            await store.RemoveAsync(name);
        }
    }

    [Test]
    public async Task Read_GivesWhatWasSaved()
    {
        var store = Store();

        await SaveAsync(store, "rclone-config", "pässwörd 🔑");

        Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("pässwörd 🔑"));
    }

    [Test]
    public async Task Save_PutsItInCredentialManager_AsUtf16_KeptToThisComputer()
    {
        await SaveAsync(Store(), "rclone-config", "pässwörd 🔑");

        var credential = InCredentialManager("rclone-config");

        Assert.That(credential, Is.EqualTo(("rclone-config", Encoding.Unicode.GetBytes("pässwörd 🔑"), CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE)));
    }

    [Test]
    public async Task Read_ACredentialCmdkeySaved()
    {
        var store = Store();
        _saved.Add((store, "rclone-config"));

        Cmdkey($"/generic:{_prefix}/rclone-config", "/user:rclone-config", "/pass:pässwörd");

        Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("pässwörd"));
    }

    [Test]
    public async Task Read_IsNullWhenNothingWasSaved() => Assert.That(await Store().ReadAsync("rclone-config"), Is.Null);

    [Test]
    public async Task Save_ReplacesTheSecret()
    {
        var store = Store();

        await SaveAsync(store, "rclone-config", "first");
        await SaveAsync(store, "rclone-config", "second");

        Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("second"));
    }

    [Test]
    public async Task Save_TakesTheLongestSecretCredentialManagerHolds()
    {
        var store = Store();
        var longest = new string('x', CredentialManager.MaxLength);

        await SaveAsync(store, "rclone-config", longest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo(longest));
            Assert.That(() => store.SaveAsync("rclone-config", new Secret(longest + "x")), Throws.ArgumentException);
        }
    }

    [Test]
    public async Task Remove_RemovesTheSecret()
    {
        var store = Store();
        await SaveAsync(store, "rclone-config", "secret");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await store.RemoveAsync("rclone-config"), Is.True);
            Assert.That(await store.ReadAsync("rclone-config"), Is.Null);
            Assert.That(await store.RemoveAsync("rclone-config"), Is.False);
        }
    }

    [Test]
    public async Task Remove_TakesItOutOfCredentialManager()
    {
        var store = Store();
        await SaveAsync(store, "rclone-config", "secret");

        await store.RemoveAsync("rclone-config");

        Assert.That(InCredentialManager("rclone-config"), Is.Null);
    }

    [Test]
    public async Task Stores_WithDifferentPrefixesKeepTheirOwnSecrets()
    {
        var store = Store();
        var other = new CredentialManager(_prefix + "-other");

        await SaveAsync(store, "rclone-config", "this home's");
        await SaveAsync(other, "rclone-config", "another home's");

        Assert.That((await store.ReadAsync("rclone-config"))?.Reveal(), Is.EqualTo("this home's"));
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

    private async Task SaveAsync(CredentialManager store, string name, string value)
    {
        _saved.Add((store, name));
        await store.SaveAsync(name, new Secret(value));
    }
}
