using System.Text;

namespace OpenBackupManager.Rclone.Tests;

// Through rclone's local backend, on a folder of the test's own
public class OperationsTests
{
    private string _folder = null!;
    private Rcd? _rcd;

    private string Top => Path.Combine(_folder, "top");

    private string Fs => "remote:" + Top;

    private RcClient Client => _rcd!.Client;

    [SetUp]
    public async Task SetUp()
    {
        _folder = Directory.CreateTempSubdirectory("obm-ops-").FullName;
        Directory.CreateDirectory(Top);
        var config = Path.Combine(_folder, "rclone.conf");
        await File.WriteAllTextAsync(config, "[remote]\ntype = local\n");
        _rcd = await Rcd.StartAsync(
            new RcdOptions(
                BundledRclone.FilePath,
                Path.Combine(_folder, "rclone.sock"),
                config,
                Path.Combine(_folder, "cache"),
                Path.Combine(_folder, "rclone.log"),
                "OpenBackupManager-Tests"),
            CancellationToken.None);
    }

    // On Windows, something else, such as an antivirus scan, can hold rclone's log for a moment after it exits
    [TearDown]
    public async Task TearDown()
    {
        if (_rcd is not null)
        {
            await _rcd.DisposeAsync();
            _rcd = null;
        }

        for (var tries = 1; ; tries++)
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
                return;
            }
            catch (IOException) when (tries < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Test]
    public async Task List_GivesWhatsDirectlyInAFolder()
    {
        Directory.CreateDirectory(Path.Combine(Top, "Work", "Sub", "Deeper"));
        await File.WriteAllTextAsync(Path.Combine(Top, "Work", "a.txt"), "hi\n");

        var items = await Client.ListAsync(Fs, "Work", dirsOnly: false, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                items?.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => (item.Name, item.IsDir)),
                Is.EqualTo<(string, bool)[]>([("Sub", true), ("a.txt", false)]));
            // Not a folder's, which is the filesystem's own on Linux and macOS
            Assert.That(items?.Single(item => item.Name == "a.txt").Size, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task List_OfOnlyFolders_LeavesOutFiles()
    {
        Directory.CreateDirectory(Path.Combine(Top, "Sub"));
        await File.WriteAllTextAsync(Path.Combine(Top, "a.txt"), "hi\n");

        var items = await Client.ListAsync(Fs, "", dirsOnly: true, CancellationToken.None);

        Assert.That(items?.Select(item => item.Name), Is.EqualTo<string[]>(["Sub"]));
    }

    [Test]
    public async Task List_OfAFolderThatIsntThere_IsNull() =>
        Assert.That(await Client.ListAsync(Fs, "Nowhere", dirsOnly: false, CancellationToken.None), Is.Null);

    [Test]
    public async Task Stat_OfNothing_IsNull() =>
        Assert.That(await Client.StatAsync(Fs, "Nowhere", CancellationToken.None), Is.Null);

    [Test]
    public async Task AFileUploaded_IntoFoldersMade_CanBeFound_ThenDeleted()
    {
        var content = Encoding.UTF8.GetBytes("Checking it can hold files\n");

        await Client.MkdirAsync(Fs, "New/Deeper", CancellationToken.None);
        await Client.UploadFileAsync(Fs, "New/Deeper", ".obm-write-test-x", content, CancellationToken.None);
        var uploaded = await Client.StatAsync(Fs, "New/Deeper/.obm-write-test-x", CancellationToken.None);
        await Client.DeleteFileAsync(Fs, "New/Deeper/.obm-write-test-x", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(uploaded?.Size, Is.EqualTo(content.Length));
            Assert.That(await Client.StatAsync(Fs, "New/Deeper/.obm-write-test-x", CancellationToken.None), Is.Null);
            Assert.That(Directory.EnumerateFileSystemEntries(Path.Combine(Top, "New", "Deeper")), Is.Empty);
        }
    }

    // Case as rclone's local backend takes it from the OS, not the filesystem
    [Test]
    public async Task FsInfo_SaysWhetherItsLocal_AndIgnoresCase()
    {
        var features = (await Client.FsInfoAsync(Fs, CancellationToken.None)).Features;

        Assert.That(features, Is.EqualTo(new FsFeatures(CaseInsensitive: !OperatingSystem.IsLinux(), IsLocal: true)));
    }
}
