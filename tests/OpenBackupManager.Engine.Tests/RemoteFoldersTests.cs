using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine.Tests;

// The write check, against an rclone that fails as each test needs, where the rest is tested through setup
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
        await using var rc = new FakeRc(Socket, name => name == "operations/uploadfile" ? (500, Error("disk full")) : (200, Done));
        using var rclone = new RcClient(Socket);

        Assert.That(
            () => new RemoteFolders(rclone, "nas:", fullPathsOnly: false).WriteTestAsync("Backups/", CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.Contains("disk full"));
        Assert.That(rc.Calls.Select(call => call.Method), Does.Contain("operations/deletefile"));
    }

    // Rather than the delete's, which fails too when the file never got there
    [Test]
    public async Task AFailedCheck_KeepsItsReason_WhenTheDeleteFailsToo()
    {
        await using var rc = new FakeRc(Socket, name => name switch
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
        await using var rc = new FakeRc(Socket, name =>
        {
            if (name == "operations/stat")
            {
                cancelling.Cancel();
            }

            return (200, Done);
        });
        using var rclone = new RcClient(Socket);

        Assert.That(
            () => new RemoteFolders(rclone, "nas:", fullPathsOnly: false).WriteTestAsync("Backups/", cancelling.Token),
            Throws.Exception);
        Assert.That(rc.Calls.Select(call => call.Method), Does.Contain("operations/deletefile"));
    }

    private static string Error(string message) => $"{{\"error\":\"{message}\"}}";
}
