using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine.Tests;

// Against an rclone that answers as each test needs, for what a real one can't be made to give, where the rest is
// tested through setup
public class RemoteFoldersTests
{
    private const string Done = "{}";

    private string _folder = null!;

    private string Socket => Path.Combine(_folder, "rc.sock");

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-folders-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, recursive: true);

    // As a write that fails may still have left the file
    [Test]
    public async Task AWriteThatFails_IsStillDeleted()
    {
        await using var rc = FakeHttp.OnSocket(Socket, path => path == "operations/uploadfile" ? (500, Error("disk full")) : (200, Done));
        using var rclone = new RcClient(Socket);

        Assert.That(
            () => new RemoteFolders(rclone, "nas:", fullPathsOnly: false).WriteTestAsync("Backups/", CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.Contains("disk full"));
        Assert.That(rc.Calls.Select(call => call.Path), Does.Contain("operations/deletefile"));
    }

    // Rather than the delete's, which fails too when the file never got there
    [Test]
    public async Task AFailedCheck_KeepsItsReason_WhenTheDeleteFailsToo()
    {
        await using var rc = FakeHttp.OnSocket(Socket, path => path switch
        {
            "operations/stat" => (500, Error("not there")),
            "operations/deletefile" => (500, Error("can't delete")),
            _ => (200, Done),
        });
        using var rclone = new RcClient(Socket);

        Assert.That(
            () => new RemoteFolders(rclone, "nas:", fullPathsOnly: false).WriteTestAsync("Backups/", CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.Contains("not there"));
    }

    // Such as when the client that asked has gone
    [Test]
    public async Task ACheckCancelledPartWay_StillDeletes()
    {
        using var cancelling = new CancellationTokenSource();
        await using var rc = FakeHttp.OnSocket(Socket, path =>
        {
            if (path == "operations/stat")
            {
                cancelling.Cancel();
            }

            return (200, Done);
        });
        using var rclone = new RcClient(Socket);

        Assert.That(
            () => new RemoteFolders(rclone, "nas:", fullPathsOnly: false).WriteTestAsync("Backups/", cancelling.Token),
            Throws.Exception);
        Assert.That(rc.Calls.Select(call => call.Path), Does.Contain("operations/deletefile"));
    }

    // As macOS can't hold both forms of an accented name in one folder, to test it there too
    [Test]
    public async Task AFolderInBothForms_IsFoundAsGiven()
    {
        var composed = "Caf\u00e9";
        var decomposed = "Cafe\u0301";
        await using var rc = FakeHttp.OnSocket(Socket, path => path switch
        {
            "operations/fsinfo" => (200, """{"features":{"caseInsensitive":false,"isLocal":false}}"""),
            _ => (200, $$"""{"list":[{"path":"{{decomposed}}","name":"{{decomposed}}","isDir":true},{"path":"{{composed}}","name":"{{composed}}","isDir":true}]}"""),
        });
        using var rclone = new RcClient(Socket);

        var found = await new RemoteFolders(rclone, "nas:", fullPathsOnly: false).FindAsync(composed, CancellationToken.None);

        Assert.That(found.Path, Is.EqualTo(composed + "/"));
    }

    // As S3's and the others' folders are only part of their files' names, so one that isn't there lists as empty
    [Test]
    public async Task AFolderThatIsntThere_InABucket_IsMissing()
    {
        await using var rc = FakeHttp.OnSocket(
            Socket,
            """{"features":{"caseInsensitive":false,"isLocal":false,"bucketBased":true}}""",
            """{"list":[{"path":"bucket","name":"bucket","isDir":true}]}""",
            """{"list":[{"path":"bucket/Backups","name":"Backups","isDir":true}]}""",
            """{"list":[]}""");
        using var rclone = new RcClient(Socket);

        var found = await new RemoteFolders(rclone, "s3:", fullPathsOnly: false).FindAsync("bucket/Bakups", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found.Folders, Is.Null);
            Assert.That(found.NearMiss, Is.EqualTo("bucket/Backups/"));
            Assert.That(found.AboveUnlisted, Is.False);
        }
    }

    // Such as SFTP to a Windows server, which rclone can't tell ignores case
    [Test]
    public async Task AFolderInAnotherCase_ThatListsAsTyped_TakesItsOwnCase()
    {
        await using var rc = FakeHttp.OnSocket(
            Socket,
            """{"features":{"caseInsensitive":false,"isLocal":false,"bucketBased":false}}""",
            """{"list":[{"path":"Backups","name":"Backups","isDir":true}]}""",
            """{"list":[]}""");
        using var rclone = new RcClient(Socket);

        var found = await new RemoteFolders(rclone, "nas:", fullPathsOnly: false).FindAsync("backups", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found.Path, Is.EqualTo("Backups/"));
            Assert.That(found.AboveUnlisted, Is.False);
        }
    }

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";
}
