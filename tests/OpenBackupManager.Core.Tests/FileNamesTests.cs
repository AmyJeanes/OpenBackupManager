using System.Runtime.InteropServices;

namespace OpenBackupManager.Core.Tests;

public class FileNamesTests
{
    private static readonly OSPlatform[] AllPlatforms = [OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux];

    [TestCase("notes.txt")]
    [TestCase(".config")]
    [TestCase("console")]
    [TestCase("my.con")]
    [TestCase("com10")]
    [TestCase("naïve résumé")]
    public void AnOrdinaryName_IsFineOnAllPlatforms(string name) =>
        Assert.That(AllPlatforms.Select(os => FileNames.Check(name, os)), Has.All.Null);

    [TestCase("", FileNameProblemKind.Empty)]
    [TestCase(".", FileNameProblemKind.Dots)]
    [TestCase("..", FileNameProblemKind.Dots)]
    public void NoName_IsAProblemOnAllPlatforms(string name, FileNameProblemKind kind) =>
        Assert.That(AllPlatforms.Select(os => FileNames.Check(name, os)), Has.All.EqualTo(new FileNameProblem(kind)));

    [TestCase('/')]
    [TestCase('\0')]
    public void ASlashOrNull_IsAProblemOnAllPlatforms(char c) =>
        Assert.That(
            AllPlatforms.Select(os => FileNames.Check($"a{c}b", os)),
            Has.All.EqualTo(new FileNameProblem(FileNameProblemKind.Character, c)));

    [TestCase('\\')]
    [TestCase(':')]
    [TestCase('*')]
    [TestCase('?')]
    [TestCase('"')]
    [TestCase('<')]
    [TestCase('>')]
    [TestCase('|')]
    [TestCase('\t')]
    [TestCase('\u001f')]
    public void ACharacterOnlyWindowsRefuses_IsAProblemOnlyThere(char c)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FileNames.Check($"a{c}b", OSPlatform.Windows), Is.EqualTo(new FileNameProblem(FileNameProblemKind.Character, c)));
            Assert.That(FileNames.Check($"a{c}b", OSPlatform.Linux), Is.Null);
            Assert.That(FileNames.Check($"a{c}b", OSPlatform.OSX), Is.Null);
        }
    }

    [TestCase("con")]
    [TestCase("PRN")]
    [TestCase("Aux")]
    [TestCase("nul.txt")]
    [TestCase("NUL.tar.gz")]
    [TestCase("con .txt")]
    [TestCase("com0")]
    [TestCase("COM9")]
    [TestCase("lpt1.log")]
    [TestCase("LPT³")]
    public void ADeviceName_IsAProblemOnlyOnWindows(string name)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FileNames.Check(name, OSPlatform.Windows), Is.EqualTo(new FileNameProblem(FileNameProblemKind.DeviceName)));
            Assert.That(FileNames.Check(name, OSPlatform.Linux), Is.Null);
        }
    }

    [TestCase("notes ")]
    [TestCase("notes.")]
    public void EndingWithASpaceOrDot_IsAProblemOnlyOnWindows(string name)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FileNames.Check(name, OSPlatform.Windows), Is.EqualTo(new FileNameProblem(FileNameProblemKind.EndsWithSpaceOrDot)));
            Assert.That(FileNames.Check(name, OSPlatform.OSX), Is.Null);
        }
    }

    [Test]
    public void TooLong_CountsAsEachOSDoes()
    {
        // 255 UTF-16 units, but 510 UTF-8 bytes
        var name = new string('é', 255);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(FileNames.Check(name, OSPlatform.Windows), Is.Null);
            Assert.That(FileNames.Check(name, OSPlatform.Linux), Is.EqualTo(new FileNameProblem(FileNameProblemKind.TooLong)));
            Assert.That(FileNames.Check(name + "e", OSPlatform.Windows), Is.EqualTo(new FileNameProblem(FileNameProblemKind.TooLong)));
            Assert.That(FileNames.Check(new string('e', 255), OSPlatform.OSX), Is.Null);
        }
    }

    [Test]
    public void ByDefault_ItsThisPlatformsRules() =>
        Assert.That(FileNames.Check("a:b"), OperatingSystem.IsWindows() ? Is.EqualTo(new FileNameProblem(FileNameProblemKind.Character, ':')) : Is.Null);

    [TestCase("con", FileNameProblemKind.DeviceName)]
    [TestCase("a:b", FileNameProblemKind.Character)]
    public void OnAllPlatforms_ItsAProblemIfAnyRefusesIt(string name, FileNameProblemKind kind) =>
        Assert.That(FileNames.Check(name, allPlatforms: true)?.Kind, Is.EqualTo(kind));

    // Windows refuses more, but counts UTF-16 units, so 200 é, 400 UTF-8 bytes, is only too long elsewhere
    [Test]
    public void OnAllPlatforms_TooLongAnywhere_IsTooLong() =>
        Assert.That(FileNames.Check(new string('é', 200), allPlatforms: true), Is.EqualTo(new FileNameProblem(FileNameProblemKind.TooLong)));
}
