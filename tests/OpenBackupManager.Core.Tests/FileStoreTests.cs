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
    public void DeleteHome() => Directory.Delete(_home, recursive: true);

    [Test]
    public void Read_Missing_IsNull_EvenBeforeTheFileExists() => Assert.That(_store.Read("rclone"), Is.Null);

    [Test]
    public void Save_ThenRead_GivesItBack()
    {
        _store.Save("rclone", new Secret("first"));
        _store.Save("rclone", new Secret("second"));

        Assert.That(_store.Read("rclone")!.Reveal(), Is.EqualTo("second"));
    }

    [Test]
    public void Secrets_KeepEachOther()
    {
        _store.Save("rclone", new Secret("one"));
        _store.Save("heartbeat", new Secret("two"));
        _store.Remove("rclone");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_store.Read("rclone"), Is.Null);
            Assert.That(_store.Read("heartbeat")!.Reveal(), Is.EqualTo("two"));
        }
    }

    [Test]
    public void Save_LeavesOnlyTheFile()
    {
        _store.Save("rclone", new Secret("value"));

        Assert.That(Directory.GetFiles(Path.GetDirectoryName(_store.Path)!).Select(Path.GetFileName), Is.EqualTo((string[])["secrets.json"]));
    }

    [Test]
    public void Save_WritesAJsonObjectOfNamesToValues()
    {
        _store.Save("rclone", new Secret("one"));
        _store.Save("heartbeat", new Secret("two"));

        Assert.That(InTheFile(), Is.EquivalentTo(new Dictionary<string, string> { ["rclone"] = "one", ["heartbeat"] = "two" }));
    }

    [Test]
    public void Read_AFileWrittenByHand()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_store.Path)!);
        File.WriteAllText(_store.Path, """{ "rclone": "by hand" }""");

        Assert.That(_store.Read("rclone")!.Reveal(), Is.EqualTo("by hand"));
    }

    [Test]
    public void Remove_TakesItOutOfTheFile_AndSaysWhetherThereWasOne()
    {
        _store.Save("rclone", new Secret("one"));
        _store.Save("heartbeat", new Secret("two"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_store.Remove("rclone"), Is.True);
            Assert.That(_store.Remove("rclone"), Is.False);
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
            Assert.That(() => _store.Save("rclone", new Secret("value")), Throws.TypeOf<InvalidDataException>().With.Message.Contains(_store.Path));
            Assert.That(File.ReadAllText(_store.Path), Is.EqualTo("{ not json"));
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void OnlyThisUserCanOpenIt()
    {
        _store.Save("rclone", new Secret("value"));

        Assert.That(File.GetUnixFileMode(_store.Path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void AFileOpenedUpByHand_IsMadePrivateAgainOnTheNextSave()
    {
        _store.Save("rclone", new Secret("value"));
        File.SetUnixFileMode(_store.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        _store.Save("rclone", new Secret("value"));

        Assert.That(File.GetUnixFileMode(_store.Path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    // Read straight from the file, so a mistake the store makes both writing and reading still shows
    private Dictionary<string, string> InTheFile() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_store.Path))!;
}
