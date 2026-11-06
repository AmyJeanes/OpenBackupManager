using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli.Tests;

public class DiagnosticsTests
{
    private string _home = null!;
    private string _saveIn = null!;

    [SetUp]
    public void SetUp()
    {
        _home = Path.Combine(Path.GetTempPath(), "obm-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _saveIn = _home + "-saved";
        Directory.CreateDirectory(Path.Combine(_home, "logs"));
        Directory.CreateDirectory(_saveIn);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var folder in (string[])[_home, _saveIn])
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Test]
    public void Export_TakesTheLogsCrashReportsAndConfig_ButNotTheSecrets()
    {
        File.WriteAllText(Path.Combine(_home, "logs", "engine-20261106.log"), "Started");
        File.WriteAllText(Path.Combine(_home, "logs", "app-20261106.log"), "Started");
        File.WriteAllText(Path.Combine(_home, "config.toml"), "");
        File.WriteAllText(Path.Combine(_home, "secrets.json"), "{}");
        var report = new CrashReports(AppPaths.InHome(_home), new FakeTimeProvider(), CrashReports.Engine)
            .Write("obmd", new Run(100, DateTimeOffset.UtcNow, "1.2.3", null, 0), "Something broke");

        var (exitCode, output) = Tool.Run("obm", "diagnostics", "export", "--home", _home, "-o", _saveIn);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("It has the logs from the last 7 days, 1 crash report, config.toml and a summary."));
            Assert.That(Entries(Saved()), Is.EquivalentTo(
            [
                "summary.txt",
                "config.toml",
                "logs/app-20261106.log",
                "logs/engine-20261106.log",
                "crashes/" + Path.GetFileName(report),
                .. OperatingSystem.IsWindows() ? ["windows-events.txt"] : OperatingSystem.IsLinux() ? ["journal.txt"] : Array.Empty<string>(),
            ]));
        }
    }

    [Test]
    public void Export_TakesOutTheHomeFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        File.WriteAllText(Path.Combine(_home, "logs", "engine-20261106.log"), "Can't open " + Path.Combine(home, "notes.txt"));

        var (exitCode, output) = Tool.Run("obm", "diagnostics", "export", "--home", _home, "-o", _saveIn);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(Read(Saved(), "logs/engine-20261106.log"), Is.EqualTo("Can't open " + Path.Combine("~", "notes.txt")));
        }
    }

    [Test]
    public void Export_SummarySaysWhatStatusAndValidateSay()
    {
        File.WriteAllText(Path.Combine(_home, "config.toml"), "");

        Assert.That(Tool.Run("obm", "diagnostics", "export", "--home", _home, "-o", _saveIn).ExitCode, Is.Zero);

        var summary = Read(Saved(), "summary.txt").ReplaceLineEndings("\n");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary, Does.Contain("\nVersion: " + typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion + "\n"));
            Assert.That(summary, Does.Contain("\nobm status\nobm: the engine isn't running\n"));
            Assert.That(summary, Does.Contain("\nobm config validate\nconfig.toml has no errors or warnings\n"));
        }
    }

    [Test]
    public void Export_ToAFile_SavesItThere()
    {
        var file = Path.Combine(_saveIn, "for-the-issue.zip");

        var (exitCode, output) = Tool.Run("obm", "diagnostics", "export", "--home", _home, "--output", file);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.StartWith("Saved " + file));
            Assert.That(Entries(file), Does.Contain("summary.txt"));
        }
    }

    [Test]
    public void Export_ToAFileThatsThere_LeavesItAlone()
    {
        var file = Path.Combine(_saveIn, "mine.zip");
        File.WriteAllText(file, "Mine");

        var (exitCode, output) = Tool.Run("obm", "diagnostics", "export", "--home", _home, "-o", file);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Is.EqualTo($"obm: there's already a file at {file}"));
            Assert.That(File.ReadAllText(file), Is.EqualTo("Mine"));
        }
    }

    private string Saved() => Directory.GetFiles(_saveIn, "obm-diagnostics-*.zip").Single();

    // Without macOS's own crash reports, which depend on what else has crashed on the machine this week
    private static List<string> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return [.. archive.Entries.Select(entry => entry.FullName).Where(name => !name.StartsWith("diagnostic-reports/", StringComparison.Ordinal))];
    }

    private static string Read(string zip, string entry)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }
}
