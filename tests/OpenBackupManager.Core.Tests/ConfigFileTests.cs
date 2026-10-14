using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.Core.Tests;

public class ConfigFileTests
{
    private string _folder = "";

    private string FilePath => Path.Combine(_folder, "config.toml");

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("obm-config-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void Parse_LeavesOutSettingsAtTheirDefaults()
    {
        var result = ConfigFile.Parse("");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config, Is.EqualTo(new AppConfig()));
            Assert.That(result.Config!.Version, Is.EqualTo(AppConfig.CurrentVersion));
            Assert.That(result.Config.Updates.Prereleases, Is.Null);
            Assert.That(result.Problems, Is.Empty);
        }
    }

    [Test]
    public void Parse_ReadsEverySetting()
    {
        var result = ConfigFile.Parse("""
            version = 1

            [updates]
            prereleases = true

            [[remotes]]
            id = "onedrive"
            name = "OneDrive"
            root = "onedrive:OpenBackupManager/"
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Config!.Updates.Prereleases, Is.True);
            Assert.That(result.Config.Remotes, Is.EquivalentTo([new RemoteSettings { Id = "onedrive", Name = "OneDrive", Root = "onedrive:OpenBackupManager/" }]));
        }
    }

    [Test]
    public void Parse_AcceptsDottedKeys() =>
        Assert.That(ConfigFile.Parse("updates.prereleases = false").Config!.Updates.Prereleases, Is.False);

    [Test]
    public void Parse_SyntaxError_NamesTheLine()
    {
        var result = ConfigFile.Parse("version = 1\n[updates]\nprereleases = tru\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config, Is.Null);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Problems.First(p => p.IsError).Line, Is.EqualTo(3));
        }
    }

    [TestCase("[updates]\nprereleases = \"yes\"", 2, "updates.prereleases should be true or false")]
    [TestCase("version = \"1\"", 1, "version should be a whole number")]
    [TestCase("[[remotes]]\nid = 7", 2, "remotes.id should be text in quotes")]
    [TestCase("version = 0", 1, "version should be 1 or more")]
    [TestCase("[remotes]\nid = \"a\"", 1, "remotes should be [[remotes]]")]
    [TestCase("[[updates]]\nprereleases = true", 1, "updates should be [updates]")]
    [TestCase("[[remotes]]\nname = \"A\"", 1, "each [[remotes]] needs an id")]
    [TestCase("[[remotes]]\nid = \"a\"\n[[remotes]]\nid = \"a\"", 4, "the remote id \"a\" is used twice")]
    public void Parse_Mistake_IsAnErrorNamingTheLine(string text, int line, string message)
    {
        var result = ConfigFile.Parse(text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config, Is.Null);
            Assert.That(result.Problems, Has.One.EqualTo(new ConfigProblem(line, message, IsError: true)));
        }
    }

    [Test]
    public void Parse_SectionOfTheWrongShape_IsAnErrorNamingTheLine()
    {
        var result = ConfigFile.Parse("version = 1\nremotes = 5\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config, Is.Null);
            Assert.That(result.Problems.Single(), Has.Property(nameof(ConfigProblem.Line)).EqualTo(2).And.Property(nameof(ConfigProblem.IsError)).True);
        }
    }

    [TestCase("[updates]\nprerelease = true", 2, "updates.prerelease isn't a known setting")]
    [TestCase("colour = \"blue\"", 1, "colour isn't a known setting")]
    [TestCase("[sync]\ntransfers = 4", 1, "[sync] isn't a known section")]
    public void Parse_UnknownKey_IsOnlyAWarning(string text, int line, string message)
    {
        var result = ConfigFile.Parse(text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config, Is.Not.Null);
            Assert.That(result.Problems, Is.EquivalentTo([new ConfigProblem(line, message, IsError: false)]));
        }
    }

    [Test]
    public void Parse_NewerVersion_IsReadButMarked()
    {
        var result = ConfigFile.Parse("version = 2\n[updates]\nprereleases = true\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config!.Version, Is.EqualTo(2));
            Assert.That(result.Config.Updates.Prereleases, Is.True);
            Assert.That(result.Problems.Single().IsError, Is.False);
        }
    }

    [Test]
    public void DefaultText_HasNoProblems()
    {
        var result = ConfigFile.Parse(ConfigFile.DefaultText);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Config, Is.EqualTo(new AppConfig()));
        }
    }

    [Test]
    public void Edit_ChangesAValue_KeepingCommentsAndLayout() =>
        Assert.That(
            ConfigFile.Edit("# Mine\nversion = 1\n\n[updates]\nprereleases   = false # trying it\n", "updates", "prereleases", true),
            Is.EqualTo("# Mine\nversion = 1\n\n[updates]\nprereleases   = true # trying it\n"));

    [Test]
    public void Edit_AddsToAnExistingSection() =>
        Assert.That(
            ConfigFile.Edit("[updates]\n# about updates\n\n[[remotes]]\nid = \"a\"\n", "updates", "prereleases", true),
            Is.EqualTo("[updates]\nprereleases = true\n# about updates\n\n[[remotes]]\nid = \"a\"\n"));

    [Test]
    public void Edit_AddsTheSection() =>
        Assert.That(ConfigFile.Edit("version = 1", "updates", "prereleases", false), Is.EqualTo("version = 1\n\n[updates]\nprereleases = false\n"));

    [Test]
    public void Edit_RemovesASetting() =>
        Assert.That(
            ConfigFile.Edit("[updates]\nprereleases = true # trying it\n# kept\n", "updates", "prereleases", null),
            Is.EqualTo("[updates]\n# kept\n"));

    [Test]
    public void Edit_KeepsWindowsLineEndings() =>
        Assert.That(ConfigFile.Edit("version = 1\r\n", "updates", "prereleases", true), Is.EqualTo("version = 1\r\n\r\n[updates]\r\nprereleases = true\r\n"));

    [Test]
    public void Read_MissingFile_GivesTheDefaults()
    {
        var result = new ConfigFile(FilePath).Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config, Is.EqualTo(new AppConfig()));
            Assert.That(result.Problems, Is.Empty);
        }
    }

    [Test]
    public void CreateIfMissing_WritesTheStartingFileOnce()
    {
        var file = new ConfigFile(FilePath);
        file.CreateIfMissing();
        File.WriteAllText(FilePath, "version = 1 # mine\n");

        file.CreateIfMissing();

        Assert.That(File.ReadAllText(FilePath), Is.EqualTo("version = 1 # mine\n"));
    }

    [Test]
    public void Set_SwapsInTheNewFileAndKeepsTheOldOne()
    {
        File.WriteAllText(FilePath, "version = 1 # mine\n");

        new ConfigFile(FilePath).Set("updates", "prereleases", true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(FilePath), Is.EqualTo("version = 1 # mine\n\n[updates]\nprereleases = true\n"));
            Assert.That(File.ReadAllText(FilePath + ".bak"), Is.EqualTo("version = 1 # mine\n"));
            Assert.That(File.Exists(FilePath + ".tmp"), Is.False);
        }
    }

    [Test]
    public void Set_StartsFromTheStartingFile()
    {
        new ConfigFile(FilePath).Set("updates", "prereleases", true);

        var result = new ConfigFile(FilePath).Read();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Config!.Updates.Prereleases, Is.True);
            Assert.That(File.ReadAllText(FilePath), Does.StartWith("# Open Backup Manager's settings"));
        }
    }

    [TestCase("version = 1\nprereleases = tru\n", "can't be changed until it's fixed")]
    [TestCase("version = 2\n", "newer version")]
    public void Set_LeavesTheFileAlone_WhenItCantBeChanged(string text, string reason)
    {
        File.WriteAllText(FilePath, text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => new ConfigFile(FilePath).Set("updates", "prereleases", true), Throws.TypeOf<ConfigException>().With.Message.Contains(reason));
            Assert.That(File.ReadAllText(FilePath), Is.EqualTo(text));
        }
    }
}
