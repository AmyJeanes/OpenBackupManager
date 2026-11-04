using System.Text.Json;

namespace OpenBackupManager.Cli.Tests;

public class ConfigTests
{
    private string _home = null!;

    private string ConfigFile => Path.Combine(_home, "config.toml");

    [SetUp]
    public void SetUp() => _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Test]
    public void Reload_AnEditWithAnError_SaysItsNotApplied()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");
        File.WriteAllText(ConfigFile, "[updates]\nprereleases = \"yes\"\n");

        var (exitCode, output) = Tool.Run("obm", "config", "reload", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.Contain(string.Join(
                Environment.NewLine,
                "config.toml has an error, so its latest changes aren't applied:",
                "  Error on line 2: updates.prereleases should be true or false")));
        }
    }

    [Test]
    public void Reload_AGoodEdit_IsApplied()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");
        File.WriteAllText(ConfigFile, "secret-store = \"file\"\n");

        var (exitCode, output) = Tool.Run("obm", "config", "reload", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("config.toml's settings are applied"));
        }
    }

    [Test]
    public void Reload_Json_HasWhatItFound()
    {
        using var daemon = Tool.Start("obmd", "--home", _home);
        daemon.WaitFor("started, process");
        File.WriteAllText(ConfigFile, "[updates]\nprereleases = \"yes\"\n");

        using var obm = Tool.Start("obm", "config", "reload", "--json", "--home", _home);
        Assert.That(obm.WaitForExit(), Is.EqualTo(1), obm.Output);
        using var result = JsonDocument.Parse(obm.Stdout);

        Assert.That(result.RootElement.GetProperty("problems")[0].GetProperty("line").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public void Reload_WithNoEngine_SaysSo()
    {
        var (exitCode, output) = Tool.Run("obm", "config", "reload", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("the engine isn't running, so there's nothing to reload"));
        }
    }

    [Test]
    public void Validate_AFileWithAnError_NamesIt_WithErrorsFirst()
    {
        Directory.CreateDirectory(_home);
        var file = Path.Combine(_home, "dotfile.toml");
        File.WriteAllText(file, "colour = \"blue\"\n[updates]\nprereleases = \"yes\"\n");

        var (exitCode, output) = Tool.Run("obm", "config", "validate", "--file", file);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.Contain(string.Join(
                Environment.NewLine,
                file + " has an error:",
                "  Error on line 3: updates.prereleases should be true or false",
                "  Warning on line 1: colour isn't a known setting")));
        }
    }

    // In the order they're found, rather than errors first, as a script can sort them itself
    [Test]
    public void Validate_Json_HasEveryProblem()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(ConfigFile, "colour = \"blue\"\n[updates]\nprereleases = \"yes\"\n");

        using var obm = Tool.Start("obm", "config", "validate", "--json", "--home", _home);
        Assert.That(obm.WaitForExit(), Is.EqualTo(1), obm.Output);
        using var result = JsonDocument.Parse(obm.Stdout);

        Assert.That(
            result.RootElement.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("kind").GetString()),
            Is.EqualTo(["unknownSetting", "shouldBeTrueOrFalse"]));
    }

    [Test]
    public void Validate_AWarning_IsntAnError()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(ConfigFile, "colour = \"blue\"\n");

        var (exitCode, output) = Tool.Run("obm", "config", "validate", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("config.toml has a warning:"));
        }
    }

    // As a service would read it, as it doesn't have them
    [Test]
    public void Validate_LeavesOutEnvVars_UnlessAsked()
    {
        var bad = new Dictionary<string, string> { ["OBM_UPDATES_PRERELEASES"] = "yes" };

        var (without, _) = Tool.Run("obm", bad, "config", "validate", "--home", _home);
        var (with, output) = Tool.Run("obm", bad, "config", "validate", "--env", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(without, Is.Zero);
            Assert.That(with, Is.EqualTo(1), output);
            Assert.That(output, Does.Contain("Error in OBM_UPDATES_PRERELEASES: updates.prereleases should be true or false"));
        }
    }

    // As the engine would start with the defaults
    [Test]
    public void Validate_NoConfigYet_FindsNothingWrong()
    {
        var (exitCode, output) = Tool.Run("obm", "config", "validate", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("config.toml has no errors or warnings"));
        }
    }

    [Test]
    public void Validate_AFileThatIsntThere_SaysSo()
    {
        var file = Path.Combine(_home, "missing.toml");

        var (exitCode, output) = Tool.Run("obm", "config", "validate", "--file", file);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("there's no file at " + file));
        }
    }
}
