using System.Runtime.Versioning;
using System.Text.Json;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Core.Tests;

public class FileStoreTests
{
    private string _home = "";
    private FileStore _store = null!;

    [SetUp]
    public void CreateStore()
    {
        _home = Directory.CreateTempSubdirectory("obm-secrets-").FullName;
        _store = new FileStore(Path.Combine(_home, "state", "secrets.json"));
    }

    [TearDown]
    public void DeleteHome()
    {
        _store.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    [Test]
    public async Task Read_Missing_IsNull_EvenBeforeTheFileExists() => Assert.That(await _store.ReadAsync("rclone"), Is.Null);

    [Test]
    public async Task Save_ThenRead_GivesItBack()
    {
        await _store.SaveAsync("rclone", new Secret("first"));
        await _store.SaveAsync("rclone", new Secret("second"));

        Assert.That((await _store.ReadAsync("rclone"))!.Reveal(), Is.EqualTo("second"));
    }

    [Test]
    public async Task Secrets_KeepEachOther()
    {
        await _store.SaveAsync("rclone", new Secret("one"));
        await _store.SaveAsync("heartbeat", new Secret("two"));
        await _store.RemoveAsync("rclone");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _store.ReadAsync("rclone"), Is.Null);
            Assert.That((await _store.ReadAsync("heartbeat"))!.Reveal(), Is.EqualTo("two"));
        }
    }

    [Test]
    public async Task Save_LeavesOnlyTheFile()
    {
        await _store.SaveAsync("rclone", new Secret("value"));

        Assert.That(Directory.GetFiles(Path.GetDirectoryName(_store.Path)!).Select(Path.GetFileName), Is.EqualTo((string[])["secrets.json"]));
    }

    [Test]
    public async Task Save_WritesAJsonObjectOfNamesToValues()
    {
        await _store.SaveAsync("rclone", new Secret("one"));
        await _store.SaveAsync("heartbeat", new Secret("two"));

        Assert.That(InTheFile(), Is.EquivalentTo(new Dictionary<string, string> { ["rclone"] = "one", ["heartbeat"] = "two" }));
    }

    [Test]
    public async Task Read_AFileWrittenByHand()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, """{ "rclone": "by hand" }""");

        Assert.That((await _store.ReadAsync("rclone"))!.Reveal(), Is.EqualTo("by hand"));
    }

    [Test]
    public async Task Remove_TakesItOutOfTheFile_AndSaysWhetherThereWasOne()
    {
        await _store.SaveAsync("rclone", new Secret("one"));
        await _store.SaveAsync("heartbeat", new Secret("two"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _store.RemoveAsync("rclone"), Is.True);
            Assert.That(await _store.RemoveAsync("rclone"), Is.False);
            Assert.That(InTheFile(), Is.EquivalentTo(new Dictionary<string, string> { ["heartbeat"] = "two" }));
        }
    }

    [Test]
    public void AFileThatIsntValidJson_IsRefused_NotReplaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, "{ not json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => _store.SaveAsync("rclone", new Secret("value")), Throws.TypeOf<InvalidDataException>().With.Message.Contains(_store.Path));
            Assert.That(File.ReadAllText(_store.Path), Is.EqualTo("{ not json"));
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public async Task OnlyThisUserCanOpenIt()
    {
        await _store.SaveAsync("rclone", new Secret("value"));

        Assert.That(File.GetUnixFileMode(_store.Path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public async Task AFileOpenedUpByHand_IsMadePrivateAgainOnTheNextSave()
    {
        await _store.SaveAsync("rclone", new Secret("value"));
        File.SetUnixFileMode(_store.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        await _store.SaveAsync("rclone", new Secret("value"));

        Assert.That(File.GetUnixFileMode(_store.Path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    // Read straight from the file, so a mistake the store makes both writing and reading still shows
    private Dictionary<string, string> InTheFile() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_store.Path))!;
}
