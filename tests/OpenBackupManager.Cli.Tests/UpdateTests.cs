using System.Runtime.Versioning;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli.Tests;

// obm update against a local server answering as GitHub does, on a copy of obm and obmd it can replace
[Platform(Exclude = "Win")]
[UnsupportedOSPlatform("windows")]
public class UpdateTests
{
    private static readonly string Version = AppVersion.Current.Split('+')[0];

    private static readonly string Runtime = FakeGitHub.Runtime;

    private static readonly string Tarball = $"OpenBackupManager-0.3.0-{Runtime}.tar.gz";

    private readonly List<Tool> _running = [];
    private string _programs = null!;
    private string _home = null!;
    private FakeGitHub _github = null!;

    private string Obm => Path.Combine(_programs, "obm");

    private string Obmd => Path.Combine(_programs, "obmd");

    [SetUp]
    public void SetUp()
    {
        var name = "obm-tests-" + Guid.NewGuid().ToString("N")[..8];
        _programs = Path.Combine(Path.GetTempPath(), name, "programs");
        _home = Path.Combine(Path.GetTempPath(), name, "home");
        Directory.CreateDirectory(_programs);
        Directory.CreateDirectory(_home);
        Tool.CopyTo(_programs);
        _github = new FakeGitHub();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var tool in _running)
        {
            tool.Dispose();
        }

        _github.Dispose();
        File.SetUnixFileMode(_programs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(Path.GetDirectoryName(_programs)!, recursive: true);
    }

    // And the licences only if they were unpacked there too
    [Test]
    public void Update_ReplacesThemWithTheNewest()
    {
        var obmd = File.GetLastWriteTimeUtc(Obmd);
        File.WriteAllText(Path.Combine(_programs, "LICENSE"), "The old version's LICENSE");
        File.Delete(Path.Combine(_programs, "THIRD-PARTY-NOTICES.txt"));
        _github.Add("0.2.0", new Asset($"OpenBackupManager-0.2.0-{Runtime}.tar.gz", FakeGitHub.NewVersionOf(_programs)));
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)), new Asset($"OpenBackupManager-0.3.0-{Runtime}.deb", [1]));

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain($"Updating from {Version} to 0.3.0"));
            Assert.That(output, Does.Contain($"Downloaded {Tarball}, and it matches what GitHub has for it"));
            Assert.That(output, Does.Contain("Updated obm and obmd in "));
            Assert.That(output, Does.Contain("The engine isn't running, so it'll run 0.3.0 when it next starts"));
            Assert.That(File.ReadAllText(Path.Combine(_programs, "LICENSE")), Is.EqualTo("The new version's LICENSE"));
            Assert.That(Path.Combine(_programs, "THIRD-PARTY-NOTICES.txt"), Does.Not.Exist);
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd.AddMinutes(1)).Within(TimeSpan.FromSeconds(1)));
            Assert.That(Leftovers(), Is.Empty);
            Assert.That(Tool.Run(Obm, "--version").ExitCode, Is.Zero);
        }
    }

    [Test]
    public void Update_WithTheEngineStartedByHand_SaysToRestartIt()
    {
        var daemon = Tool.Start(Obmd, "--home", _home);
        _running.Add(daemon);
        daemon.WaitFor("started, process");
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain($"The engine is still running {Version}. Restart it to run 0.3.0"));
        }
    }

    [Test]
    public void Check_SaysThereIsANewerOne_AndChangesNothing()
    {
        var obmd = File.GetLastWriteTimeUtc(Obmd);
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update("--check");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Is.EqualTo($"0.3.0 is out, and this is {Version}. Run obm update to update"));
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd));
        }
    }

    [TestCase("--check")]
    [TestCase(null)]
    public void Update_WithNothingNewer_SaysItsTheNewest(string? check)
    {
        var (exitCode, output) = Update(check is null ? [] : [check]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Is.EqualTo($"This is the newest version, {Version}"));
        }
    }

    [TestCase("true", "0.4.0-beta.1")]
    [TestCase("false", "0.3.0")]
    public void Prereleases_FollowTheSetting(string prereleases, string expected)
    {
        File.WriteAllText(Path.Combine(_home, "config.toml"), $"[updates]\nprereleases = {prereleases}\n");
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));
        _github.Add("0.4.0-beta.1", new Asset($"OpenBackupManager-0.4.0-beta.1-{Runtime}.tar.gz", FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update("--check");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.StartWith($"{expected} is out"));
        }
    }

    [TestCase("deb", "It was installed from a .deb, which needs root to update, so run:", "sudo apt install ./OpenBackupManager-0.3.0-{0}.deb")]
    [TestCase("rpm", "It was installed from a .rpm, which needs root to update, so run:", "sudo dnf install https://github.com/AmyJeanes/OpenBackupManager/releases/download/v0.3.0/OpenBackupManager-0.3.0-{0}.rpm")]
    [TestCase("docker", "In Docker, update the image instead", "docker compose pull")]
    [TestCase("Homebrew", "It was installed by Homebrew, so update it the same way", "")]
    public void Update_OfAnotherKindOfInstall_SaysHowInstead(string by, string how, string command)
    {
        File.WriteAllText(Path.Combine(_programs, "installed-by"), by + "\n");
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update();
        var (checkExitCode, checkOutput) = Update("--check");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.StartWith($"obm: 0.3.0 is out, and this is {Version}. {how}"));
            Assert.That(output, Does.Contain(string.Format(null, command, Runtime)));
            Assert.That(checkExitCode, Is.Zero, checkOutput);
            Assert.That(checkOutput, Is.EqualTo(output));
        }
    }

    [Test]
    public void Update_WhenTheDownloadDoesntMatch_ChangesNothing()
    {
        var obmd = File.GetLastWriteTimeUtc(Obmd);
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)) { Digest = "sha256:" + new string('0', 64) });

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.Contain($"obm: {Tarball} doesn't match what GitHub has for it, so nothing was changed"));
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd));
            Assert.That(Leftovers(), Is.Empty);
        }
    }

    [Test]
    public void Update_WithoutAChecksum_ChangesNothing()
    {
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)) { HasDigest = false });

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo($"obm: GitHub doesn't give a checksum for {Tarball}, so it can't be checked, and nothing was changed"));
        }
    }

    [Test]
    public void Update_WithNoDownloadForThisComputer_SaysToTryLater()
    {
        _github.Add("0.3.0", new Asset("OpenBackupManager-0.3.0-plan9-x64.tar.gz", FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo($"obm: 0.3.0 has no download for {Runtime} yet, so nothing was changed. Try again later"));
        }
    }

    [Test]
    public void Update_WhereItCantWrite_SaysWhatToDo()
    {
        Assume.That(Environment.UserName, Is.Not.EqualTo("root"), "root can write anywhere");
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));
        File.SetUnixFileMode(_programs, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.StartWith("obm: can't write to ").And.EndWith(
                ", so obm and obmd can't be updated there. Unpack the new version there yourself, or move them to a folder of yours, such as ~/.local/bin"));
        }
    }

    [Test]
    public void Update_WithoutObmdBesideIt_SaysToUnpackItYourself()
    {
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));
        File.Delete(Obmd);

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.StartWith("obm: obmd isn't beside obm in ").And.EndWith(", so they can't be updated together. Unpack the new version yourself"));
        }
    }

    [Test]
    public void Update_WhenGitHubIsLimitingRequests_SaysSo()
    {
        _github.RateLimited = true;

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo("obm: GitHub is limiting how often this computer can check for updates. Try again within the hour"));
        }
    }

    [Test]
    public void Update_WhenGitHubCantBeReached_SaysSo()
    {
        var (exitCode, output) = Tool.Run(Obm, new Dictionary<string, string> { ["OBM_UPDATE_SOURCE"] = "http://127.0.0.1:9" }, "update", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.StartWith("obm: couldn't reach GitHub to check for updates. "));
        }
    }

    // A local source is how a build of your own is tried, so only GitHub is refused
    [Test]
    public void Update_OfABuildOfYourOwn_IsRefused()
    {
        Assume.That(Version, Is.EqualTo("0.0.0"), "a release's build");

        var (exitCode, output) = Tool.Run(Obm, "update", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo("obm: this is a build of your own (0.0.0), so obm update won't replace it with a release"));
        }
    }

    // What it unpacks and checks it can write, which it removes
    private string[] Leftovers() => [.. Directory.GetFiles(_programs, ".*.update"), .. Directory.GetFiles(_programs, ".obm-update-*")];

    private (int ExitCode, string Output) Update(params string[] args) =>
        Tool.Run(Obm, _github.Environment, ["update", "--home", _home, .. args]);
}

public class UpdateOnWindowsTests
{
    [Test]
    [Platform("Win")]
    public void Update_SaysTheAppUpdatesItself()
    {
        var (exitCode, output) = Tool.Run("obm", "update");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Is.EqualTo("obm: on Windows, the app updates itself, and obm with it. To check now, use Check for updates in About"));
        }
    }

    [Test]
    [Platform("Win")]
    public void Update_IsntListed() => Assert.That(Tool.Run("obm", "--help").Output, Does.Not.Contain("update"));
}
