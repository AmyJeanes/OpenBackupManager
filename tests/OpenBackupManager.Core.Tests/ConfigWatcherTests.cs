using Microsoft.Extensions.Logging.Abstractions;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Core.Tests;

public class ConfigWatcherTests
{
    // Only a guard against a hang: the tests wait for the watcher's own event
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private string _folder = "";
    private ConfigFile _file = null!;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Directory.CreateTempSubdirectory("obm-watcher-").FullName;
        _file = new ConfigFile(Path.Combine(_folder, "config.toml"));
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void StartsWithTheFilesSettings()
    {
        Write("[updates]\nprereleases = true\n");

        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        Assert.That(watcher.Config.Updates.Prereleases, Is.True);
    }

    [Test]
    public void StartsWithTheDefaults_WhenThereIsNoFile()
    {
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        Assert.That(watcher.Config, Is.EqualTo(new AppConfig()));
    }

    [Test]
    public void WontStart_WhenTheFileHasAnError()
    {
        Write("version = 1\n[updates]\nprereleases = \"yes\"\n");

        Assert.That(
            () => new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance),
            Throws.TypeOf<ConfigException>()
                .With.Message.EqualTo($"{_file.Path} has an error on line 3: updates.prereleases should be true or false")
                .And.Property(nameof(ConfigException.Problem)).Property(nameof(ConfigProblem.Line)).EqualTo(3));
    }

    [Test]
    public void WontStart_WhenAnEnvVarHasAMistake() =>
        Assert.That(
            () => new ConfigWatcher(new ConfigFile(_file.Path, name => name == "OBM_SECRET_STORE" ? "vault" : null), NullLogger<ConfigWatcher>.Instance),
            Throws.TypeOf<ConfigException>().With.Message.EqualTo(
                $"OBM_SECRET_STORE has an error: secret-store should be one of: {string.Join(", ", SecretStoreKinds.NamesOn(CurrentOS.Platform))}"));

    [Test]
    public void AppliesAnEditWhenItsSaved()
    {
        Write("[updates]\nprereleases = false\n");
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        var result = WaitForChange(watcher, () => Write("[updates]\nprereleases = true\n"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(watcher.Config.Updates.Prereleases, Is.True);
        }
    }

    [Test]
    public void KeepsTheEnvVars_OverAnEdit()
    {
        Write("[updates]\nprereleases = false\n");
        using var watcher = new ConfigWatcher(new ConfigFile(_file.Path, name => name == "OBM_UPDATES_AUTOMATIC" ? "false" : null), NullLogger<ConfigWatcher>.Instance);

        WaitForChange(watcher, () => Write("[updates]\nprereleases = true\nautomatic = true\n"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Config.Updates.Prereleases, Is.True);
            Assert.That(watcher.Config.Updates.Automatic, Is.False);
        }
    }

    [Test]
    public void KeepsTheLastGoodSettings_UntilAnEditIsFixed()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        var broken = WaitForChange(watcher, () => Write("[updates]\nprereleases = tru\n"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(broken.Problems.Single(p => p.IsError).Line, Is.EqualTo(2));
            Assert.That(watcher.Config.Updates.Prereleases, Is.True);
        }

        WaitForChange(watcher, () => Write("[updates]\nprereleases = false\n"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Result.HasErrors, Is.False);
            Assert.That(watcher.Config.Updates.Prereleases, Is.False);
        }
    }

    [Test]
    public void WaitsForASaveToFinish()
    {
        Write("[updates]\nprereleases = false\n");
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        var result = WaitForChange(watcher, () =>
        {
            using var stream = new FileStream(_file.Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            stream.Write("[updates]\nprereleases = tr"u8);
            stream.Flush();
            // Windows often reports a write only when it leaves the cache, after the save is done. This change is
            // reported straight away, so the watcher hears of the file while it's half written
            File.SetLastWriteTimeUtc(_file.Path, DateTime.UtcNow);
            Thread.Sleep(50);
            stream.Write("ue\n"u8);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(watcher.Config.Updates.Prereleases, Is.True);
        }
    }

    [Test]
    public void PicksUpTheAppsOwnChanges()
    {
        _file.CreateIfMissing();
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        WaitForChange(watcher, () => _file.Set("updates", "prereleases", true));

        Assert.That(watcher.Config.Updates.Prereleases, Is.True);
    }

    [Test]
    public void FollowsEditsToTheFileALinkPointsTo()
    {
        var dotfiles = Directory.CreateDirectory(Path.Combine(_folder, "dotfiles")).FullName;
        var kept = Path.Combine(dotfiles, "obm.toml");
        File.WriteAllText(kept, "[updates]\nprereleases = false\n");
        Link(() => File.CreateSymbolicLink(_file.Path, kept));
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        WaitForChange(watcher, () => File.WriteAllText(kept, "[updates]\nprereleases = true\n"));

        Assert.That(watcher.Config.Updates.Prereleases, Is.True);
    }

    [Test]
    public void FollowsEditsInTheFolderALinkPointsTo()
    {
        var dotfiles = Directory.CreateDirectory(Path.Combine(_folder, "dotfiles")).FullName;
        File.WriteAllText(Path.Combine(dotfiles, "config.toml"), "[updates]\nprereleases = false\n");
        var linked = Path.Combine(_folder, "linked");
        Link(() => Directory.CreateSymbolicLink(linked, dotfiles));
        using var watcher = new ConfigWatcher(new ConfigFile(Path.Combine(linked, "config.toml")), NullLogger<ConfigWatcher>.Instance);

        WaitForChange(watcher, () => File.WriteAllText(Path.Combine(dotfiles, "config.toml"), "[updates]\nprereleases = true\n"));

        Assert.That(watcher.Config.Updates.Prereleases, Is.True);
    }

    [Test]
    public void GoesBackToTheDefaults_WhenTheFileIsDeleted()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);

        WaitForChange(watcher, () => File.Delete(_file.Path));

        Assert.That(watcher.Config, Is.EqualTo(new AppConfig()));
    }

    [Test]
    public void IgnoresTheFileWhileItsEmpty()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);
        var changes = 0;
        watcher.Changed += _ => changes++;

        Write("");
        watcher.Reload();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes, Is.Zero);
            Assert.That(watcher.Config.Updates.Prereleases, Is.True);
        }
    }

    [Test]
    public void DoesNothing_WhenTheTextHasntChanged()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file, NullLogger<ConfigWatcher>.Instance);
        var changes = 0;
        watcher.Changed += _ => changes++;

        watcher.Reload();
        watcher.Reload();

        Assert.That(changes, Is.Zero);
    }

    private void Write(string text) => File.WriteAllText(_file.Path, text);

    // Windows only lets an admin, or Developer Mode, make symbolic links. CI's runners run as admin
    private static void Link(Action create)
    {
        try
        {
            create();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Can't make symbolic links here: {e.Message}");
        }
    }

    private static ConfigResult WaitForChange(ConfigWatcher watcher, Action change)
    {
        var changed = new TaskCompletionSource<ConfigResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += result => changed.TrySetResult(result);
        change();
        Assert.That(changed.Task.Wait(Timeout), Is.True, "The watcher didn't see the change");
        return changed.Task.Result;
    }
}
