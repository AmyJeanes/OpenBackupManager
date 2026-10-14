using OpenBackupManager.Core.Configuration;

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

        using var watcher = new ConfigWatcher(_file);

        Assert.That(watcher.Config.Updates.Prereleases, Is.True);
    }

    [Test]
    public void StartsWithTheDefaults_WhenTheFileHasAnError()
    {
        Write("[updates]\nprereleases = tru\n");

        using var watcher = new ConfigWatcher(_file);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Config, Is.EqualTo(new AppConfig()));
            Assert.That(watcher.Result.HasErrors, Is.True);
        }
    }

    [Test]
    public void AppliesAnEditWhenItsSaved()
    {
        Write("[updates]\nprereleases = false\n");
        using var watcher = new ConfigWatcher(_file);

        var result = WaitForChange(watcher, () => Write("[updates]\nprereleases = true\n"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(watcher.Config.Updates.Prereleases, Is.True);
        }
    }

    [Test]
    public void KeepsTheLastGoodSettings_UntilAnEditIsFixed()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file);

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
    public void PicksUpTheAppsOwnChanges()
    {
        _file.CreateIfMissing();
        using var watcher = new ConfigWatcher(_file);

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
        using var watcher = new ConfigWatcher(_file);

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
        using var watcher = new ConfigWatcher(new ConfigFile(Path.Combine(linked, "config.toml")));

        WaitForChange(watcher, () => File.WriteAllText(Path.Combine(dotfiles, "config.toml"), "[updates]\nprereleases = true\n"));

        Assert.That(watcher.Config.Updates.Prereleases, Is.True);
    }

    [Test]
    public void GoesBackToTheDefaults_WhenTheFileIsDeleted()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file);

        WaitForChange(watcher, () => File.Delete(_file.Path));

        Assert.That(watcher.Config, Is.EqualTo(new AppConfig()));
    }

    [Test]
    public void IgnoresTheFileWhileItsEmpty()
    {
        Write("[updates]\nprereleases = true\n");
        using var watcher = new ConfigWatcher(_file);
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
        using var watcher = new ConfigWatcher(_file);
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
