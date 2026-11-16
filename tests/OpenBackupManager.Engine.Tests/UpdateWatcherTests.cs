using System.Runtime.Versioning;

namespace OpenBackupManager.Engine.Tests;

public class UpdateWatcherTests
{
    private string _folder = "";

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-watcher-").FullName;

    [TearDown]
    public void DeleteFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void Identity_IsNull_OnceObmdIsGone() =>
        Assert.That(UpdateWatcher.Identity(Path.Combine(_folder, "obmd")), Is.Null);

    // Or the engine would remove its service and stop, though obmd is still there
    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void Identity_CantBeTold_WhileObmdCantBeRead()
    {
        Assume.That(Environment.UserName, Is.Not.EqualTo("root"), "Root can read it anyway");
        var obmd = Path.Combine(_folder, "obmd");
        File.WriteAllText(obmd, "");
        File.SetUnixFileMode(_folder, UnixFileMode.None);

        Assert.That(() => UpdateWatcher.Identity(obmd), Throws.InstanceOf<UnauthorizedAccessException>());
    }
}
