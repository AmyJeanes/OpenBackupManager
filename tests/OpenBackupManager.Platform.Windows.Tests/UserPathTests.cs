using Microsoft.Win32;

namespace OpenBackupManager.Platform.Windows.Tests;

// On a key of their own rather than the user's environment, so running them never changes the real PATH. Each case is
// one that has broken other tools' PATH handling: rustup, Scoop, Chocolatey and VS Code's installer
public class UserPathTests
{
    private const string Folder = @"C:\Apps\OpenBackupManager.App\current\bin";

    private string _path = "";

    [SetUp]
    public void SetUp() => _path = @"Software\OpenBackupManager-Tests-" + Guid.NewGuid().ToString("N");

    [TearDown]
    public void TearDown() => Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);

    [Test]
    public void Add_AppendsTheFolder_KeepingEverythingElseAsItWas()
    {
        Write(@"%USERPROFILE%\bin;;C:\Tools", RegistryValueKind.ExpandString);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Add(Folder, _path), Is.True);
            Assert.That(Value(), Is.EqualTo(@$"%USERPROFILE%\bin;;C:\Tools;{Folder}"));
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.ExpandString));
        }
    }

    [TestCase(@"C:\Tools;", @"C:\Tools;" + Folder)]
    [TestCase(@"C:\Tools;;", @"C:\Tools;;" + Folder)]
    [TestCase(";", ";" + Folder)]
    public void Add_UsesATrailingSeparatorThatsThere(string before, string after)
    {
        Write(before, RegistryValueKind.ExpandString);

        UserPath.Add(Folder, _path);

        Assert.That(Value(), Is.EqualTo(after));
    }

    [Test]
    public void Add_KeepsAPlainStringPlain()
    {
        Write(@"C:\Tools", RegistryValueKind.String);

        UserPath.Add(Folder, _path);

        Assert.That(Kind(), Is.EqualTo(RegistryValueKind.String));
    }

    [Test]
    public void Add_StartsThePath_WhenThereIsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Add(Folder, _path), Is.True);
            Assert.That(Value(), Is.EqualTo(Folder));
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.ExpandString));
        }
    }

    [TestCase(Folder)]
    [TestCase(Folder + @"\")]
    [TestCase(@"C:\APPS\OpenBackupManager.App\Current\Bin")]
    [TestCase(" " + Folder + " ")]
    [TestCase("\"" + Folder + "\"")]
    public void Add_DoesNothing_WhenTheFolderIsAlreadyThere(string entry)
    {
        Write(@$"C:\Tools;{entry}", RegistryValueKind.ExpandString);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Add(Folder, _path), Is.False);
            Assert.That(Value(), Is.EqualTo(@$"C:\Tools;{entry}"));
        }
    }

    [Test]
    public void Add_StillAdds_WhenAnotherFolderStartsWithItsName()
    {
        Write(Folder + "-old", RegistryValueKind.ExpandString);

        UserPath.Add(Folder, _path);

        Assert.That(Value(), Is.EqualTo($"{Folder}-old;{Folder}"));
    }

    [Test]
    public void Remove_RemovesOnlyTheFolder_KeepingTheRestInOrder()
    {
        Write(@$"%USERPROFILE%\bin;;{Folder}\;C:\Tools;", RegistryValueKind.ExpandString);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Remove(Folder, _path), Is.True);
            Assert.That(Value(), Is.EqualTo(@"%USERPROFILE%\bin;;C:\Tools;"));
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.ExpandString));
        }
    }

    [Test]
    public void Remove_RemovesEveryCopy()
    {
        Write(@$"{Folder};C:\Tools;""{Folder}"";{Folder.ToUpperInvariant()}", RegistryValueKind.String);

        UserPath.Remove(Folder, _path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Value(), Is.EqualTo(@"C:\Tools"));
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.String));
        }
    }

    [Test]
    public void Remove_LeavesAFolderThatStartsWithItsName()
    {
        Write(@$"{Folder}-old;{Folder};C:\Tools", RegistryValueKind.ExpandString);

        UserPath.Remove(Folder, _path);

        Assert.That(Value(), Is.EqualTo(@$"{Folder}-old;C:\Tools"));
    }

    [Test]
    public void Remove_RemovesThePath_WhenNothingElseIsLeft()
    {
        Write(Folder + ";", RegistryValueKind.ExpandString);

        UserPath.Remove(Folder, _path);

        Assert.That(Value(), Is.Null);
    }

    [TestCase("")]
    [TestCase(";")]
    [TestCase(@"C:\Tools")]
    public void Remove_DoesNothing_WhenTheFolderIsntThere(string path)
    {
        Write(path, RegistryValueKind.String);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Remove(Folder, _path), Is.False);
            Assert.That(Value(), Is.EqualTo(path));
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.String));
        }
    }

    [Test]
    public void Remove_DoesNothing_WithoutThePathOrItsKey()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Remove(Folder, _path), Is.False);
            Assert.That(UserPath.Remove(Folder, _path + "-missing"), Is.False);
        }
    }

    // Writing a string over it would destroy whatever it holds
    [Test]
    public void APathThatIsntAString_IsLeftAlone()
    {
        string[] lines = [@"C:\Tools", Folder];
        using (var key = Registry.CurrentUser.CreateSubKey(_path))
        {
            key.SetValue("Path", lines, RegistryValueKind.MultiString);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Add(Folder, _path), Is.False);
            Assert.That(UserPath.Remove(Folder, _path), Is.False);
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.MultiString));
            Assert.That(RawValue(), Is.EqualTo(lines));
        }
    }

    [Test]
    public void ABinaryPath_IsLeftAlone()
    {
        byte[] bytes = [1, 2, 3];
        using (var key = Registry.CurrentUser.CreateSubKey(_path))
        {
            key.SetValue("Path", bytes, RegistryValueKind.Binary);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPath.Add(Folder, _path), Is.False);
            Assert.That(UserPath.Remove(Folder, _path), Is.False);
            Assert.That(Kind(), Is.EqualTo(RegistryValueKind.Binary));
            Assert.That(RawValue(), Is.EqualTo(bytes));
        }
    }

    private void Write(string value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_path);
        key.SetValue("Path", value, kind);
    }

    private string? Value() => RawValue() as string;

    private object? RawValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_path);
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private RegistryValueKind Kind()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_path)!;
        return key.GetValueKind("Path");
    }
}
