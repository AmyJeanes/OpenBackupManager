using Microsoft.Win32;

namespace OpenBackupManager.Platform.Windows.Tests;

// On a key of their own rather than the real Run key, so running them never starts anything at login
public class RunKeyTests
{
    private const string Exe = @"C:\Apps\OpenBackupManager.App\current\OpenBackupManager.App.Windows.exe";

    private string _path = "";

    [SetUp]
    public void SetUp() => _path = @"Software\OpenBackupManager-Tests-" + Guid.NewGuid().ToString("N");

    [TearDown]
    public void TearDown() => Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Test]
    public void TurnOn_WritesTheCommand()
    {
        var entry = Entry("home", $"\"{Exe}\" --home \"C:\\Home\" --at-login");

        entry.TurnOn();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.IsOn, Is.True);
            Assert.That(Value("home"), Is.EqualTo($"\"{Exe}\" --home \"C:\\Home\" --at-login"));
        }
    }

    [Test]
    public void TurnOff_RemovesTheEntry()
    {
        var entry = Entry("home", $"\"{Exe}\"");
        entry.TurnOn();

        entry.TurnOff();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.IsOn, Is.False);
            Assert.That(Value("home"), Is.Null);
        }
    }

    [Test]
    public void TurnOff_DoesNothingWhenItIsntThere() => Assert.DoesNotThrow(Entry("home", $"\"{Exe}\"").TurnOff);

    [Test]
    public void IsOn_IsFalseForAnotherCommand()
    {
        Entry("home", @"""C:\Old\OpenBackupManager.App.Windows.exe""").TurnOn();

        Assert.That(Entry("home", $"\"{Exe}\"").IsOn, Is.False);
    }

    [Test]
    public void RemoveFor_RemovesEveryEntryForTheExeAndNothingElse()
    {
        Entry("home", $"\"{Exe}\" --home \"C:\\Home\" --at-login").TurnOn();
        Entry("other-home", $"\"{Exe.ToUpperInvariant()}\" --home \"C:\\Other\" --at-login").TurnOn();
        Entry("dev", @"""C:\Dev\OpenBackupManager.App.Windows.exe"" --at-login").TurnOn();
        Entry("someone-else", @"""C:\Apps\Other.exe""").TurnOn();

        RunKey.RemoveFor(Exe, _path);

        using var key = Registry.CurrentUser.OpenSubKey(_path)!;
        Assert.That(key.GetValueNames(), Is.EquivalentTo(["dev", "someone-else"]));
    }

    [Test]
    public void RemoveFor_DoesNothingWithoutTheKey() => Assert.DoesNotThrow(() => RunKey.RemoveFor(Exe, _path));

    private RunKey Entry(string name, string command) => new(name, command, _path);

    private string? Value(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_path);
        return key?.GetValue(name) as string;
    }
}
