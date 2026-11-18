using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

    private string Rclone => Path.Combine(_programs, "obm-rclone");

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
        try
        {
            foreach (var tool in _running)
            {
                tool.Dispose();
            }
        }
        finally
        {
            // NUnit uses one instance for every test in the class, so a failed clean-up mustn't be left to the next
            _running.Clear();
            _github.Dispose();
            File.SetUnixFileMode(_programs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(Path.GetDirectoryName(_programs)!, recursive: true);
        }
    }

    // And the licences, as the folder is theirs alone
    [Test]
    public void Update_ReplacesThemWithTheNewest()
    {
        var obm = File.GetLastWriteTimeUtc(Obm);
        var obmd = File.GetLastWriteTimeUtc(Obmd);
        var rclone = File.GetLastWriteTimeUtc(Rclone);
        File.WriteAllText(Path.Combine(_programs, "LICENSE"), "The old version's LICENSE");
        File.Delete(Path.Combine(_programs, "THIRD-PARTY-NOTICES.txt"));
        _github.Add("0.2.0", new Asset($"OpenBackupManager-0.2.0-{Runtime}.tar.gz", FakeGitHub.NewVersionOf(_programs)));
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)), new Asset($"OpenBackupManager-0.3.0-{Runtime}.deb", [1]));

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain($"Updating from {Version} to 0.3.0"));
            Assert.That(output, Does.Contain($"Downloaded {Tarball}, and its SHA-256 hash matches the one GitHub gives for it"));
            Assert.That(output, Does.Contain("Updated obm, obmd and obm-rclone in "));
            Assert.That(output, Does.Contain("The engine isn't running, so it'll run 0.3.0 when it next starts"));
            Assert.That(File.ReadAllText(Path.Combine(_programs, "LICENSE")), Is.EqualTo("The new version's LICENSE"));
            Assert.That(File.ReadAllText(Path.Combine(_programs, "THIRD-PARTY-NOTICES.txt")), Is.EqualTo("The new version's THIRD-PARTY-NOTICES.txt"));
            Assert.That(File.GetLastWriteTimeUtc(Obm), Is.EqualTo(obm.AddMinutes(1)).Within(TimeSpan.FromSeconds(1)));
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd.AddMinutes(1)).Within(TimeSpan.FromSeconds(1)));
            Assert.That(File.GetLastWriteTimeUtc(Rclone), Is.EqualTo(rclone.AddMinutes(1)).Within(TimeSpan.FromSeconds(1)));
            Assert.That(Leftovers(), Is.Empty);
            Assert.That(Tool.Run(Obm, "--version").ExitCode, Is.Zero);
        }
    }

    // As for a copy in /usr/local/bin shared by every user, updated by root under a restrictive umask
    [Test]
    public void Update_KeepsWhoCanRunThem()
    {
        File.SetUnixFileMode(Obm, (UnixFileMode)0b111_101_101);
        File.SetUnixFileMode(Obmd, (UnixFileMode)0b111_101_101);
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Tool.Run("/bin/sh", _github.Environment, "-c", "umask 077 && exec \"$0\" update --home \"$1\"", Obm, _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain("Updated obm, obmd and obm-rclone in "));
            Assert.That(File.GetUnixFileMode(Obm), Is.EqualTo((UnixFileMode)0b111_101_101));
            Assert.That(File.GetUnixFileMode(Obmd), Is.EqualTo((UnixFileMode)0b111_101_101));
        }
    }

    // As the tarball's hash matches, it would otherwise be renamed into place there
    [Test]
    public void Update_WritesNothingOutsideItsFolder()
    {
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs, alsoNamed: "../outside")));

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_programs)!), Is.Empty);
            Assert.That(Leftovers(), Is.Empty);
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

    // Its engine waits for the error to be fixed, as a new one wouldn't start with it
    [Test]
    public void Update_WithTheServicesEngine_AndAnErrorInConfigToml_SaysItRestartsOnceFixed()
    {
        var daemon = Tool.Start(Obmd, "--home", _home, "--started-by", "service");
        _running.Add(daemon);
        daemon.WaitFor("started, process");
        File.WriteAllText(Path.Combine(_home, "config.toml"), "version = 1\n[updates]\nprereleases = \"yes\"\n");
        daemon.WaitFor("so the last good settings stay in use");
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, output);
            Assert.That(output, Does.Contain($"The engine is still running {Version}, as config.toml has an error. It restarts to run 0.3.0 once it's fixed"));
        }
    }

    // The restarted engine refuses this obm, which is the version before it
    [Test]
    public void Update_ToAnotherProtocol_WithTheServicesEngine_SaysItRestarted()
    {
        var daemon = Tool.Start(Obmd, "--home", _home, "--started-by", "service");
        _running.Add(daemon);
        daemon.WaitFor("started, process");
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        using var update = Tool.Start(Obm, _github.Environment, "update", "--home", _home);
        // For its service to start the new obmd, which this stands in for
        daemon.WaitForExit();
        using var restarted = new AnotherProtocolEngine(_home);
        var exitCode = update.WaitForExit();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, update.Output);
            Assert.That(update.Output, Does.Contain("The engine restarted and is running 0.3.0"));
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

    // As a release made by hand might be, where CI only makes them from v tags
    [Test]
    public void Update_IgnoresAReleaseWithoutAVersionTag()
    {
        _github.AddUnderTag("9.9.9", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));

        var (exitCode, output) = Update("--check");

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
            Assert.That(output, Does.Contain($"obm: the hash check failed: {Tarball}'s SHA-256 hash isn't the one GitHub gives for it, so nothing was changed"));
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd));
            Assert.That(Leftovers(), Is.Empty);
        }
    }

    // Where the umask would let others in, and the temp folder is shared, so no one else can change it before it's
    // installed
    [Test]
    public void Update_DownloadsWhereOnlyThisUserCanOpenIt()
    {
        var temp = Path.Combine(Path.GetDirectoryName(_programs)!, "temp");
        Directory.CreateDirectory(temp);
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)) { Stalls = true });

        using var update = Tool.Start("/bin/sh", new Dictionary<string, string>(_github.Environment) { ["TMPDIR"] = temp },
            "-c", "umask 0 && exec \"$0\" update --home \"$1\"", Obm, _home);
        // Beside .NET's own files there
        string[] Downloads() =>
            [.. Directory.GetFiles(temp, "obm-update-*"), .. Directory.GetDirectories(temp, "obm-update-*").SelectMany(Directory.GetFiles)];
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (Downloads().Length == 0 && !update.HasExited && DateTime.UtcNow < until)
        {
            Thread.Sleep(100);
        }

        var downloads = Downloads();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(downloads, Has.Length.EqualTo(1), update.Output);
            Assert.That(Directory.GetDirectories(temp, "obm-update-*"), Has.Length.EqualTo(1));
            Assert.That(downloads.Select(d => File.GetUnixFileMode(Path.GetDirectoryName(d)!)), Is.All.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(downloads.Select(File.GetUnixFileMode), Is.All.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
    }

    // As if it were changed between the hash check of the download and unpacking it
    [Test]
    public void Replace_WhenTheTarballIsntTheOneDownloaded_ChangesNothing()
    {
        var obmd = File.GetLastWriteTimeUtc(Obmd);
        var tarball = Path.Combine(Path.GetDirectoryName(_programs)!, Tarball);
        File.WriteAllBytes(tarball, FakeGitHub.NewVersionOf(_programs));

        var e = Assert.Throws<UpdateException>(() => Cli.Update.Replace(tarball, _programs, Tarball, "sha256:" + new string('0', 64)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(e.Message, Is.EqualTo($"the hash check failed: {Tarball} changed after it was downloaded, so nothing was changed. Try again, and report it if it keeps happening"));
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd));
            Assert.That(Leftovers(), Is.Empty);
        }
    }

    // As when Wi-Fi drops partway through
    [Test]
    public void Update_WhenTheDownloadIsCutOff_SaysSo()
    {
        var obmd = File.GetLastWriteTimeUtc(Obmd);
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)) { CutOff = true });

        var (exitCode, output) = Update();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output.Split(Environment.NewLine)[^1], Does.StartWith($"obm: couldn't download {Tarball}, so nothing was changed. "));
            Assert.That(output, Does.Not.Contain("Unhandled exception"));
            Assert.That(File.GetLastWriteTimeUtc(Obmd), Is.EqualTo(obmd));
            Assert.That(Leftovers(), Is.Empty);
        }
    }

    // As when the connection died silently, such as a laptop's lid closing partway
    [Test]
    public async Task Download_WhenNothingMoreComes_GivesUp()
    {
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)) { Stalls = true });
        using var releases = new Releases(_github.Url) { IdleTimeout = TimeSpan.FromSeconds(1) };
        var newest = await releases.NewestAsync(prereleases: false, CancellationToken.None);

        var download = releases.DownloadAsync(newest!.Assets.Single(), Path.Combine(_home, Tarball), CancellationToken.None);

        Assert.That(async () => await download.WaitAsync(TimeSpan.FromSeconds(20)), Throws.TypeOf<UpdateException>()
            .With.Message.EqualTo($"couldn't download {Tarball}, so nothing was changed. Nothing arrived for 1 seconds"));
    }

    [Test]
    public void Update_WhenTheTempFolderIsMissing_SaysSo()
    {
        _github.Add("0.3.0", new Asset(Tarball, FakeGitHub.NewVersionOf(_programs)));
        var temp = Path.Combine(Path.GetDirectoryName(_programs)!, "missing");

        var (exitCode, output) = Tool.Run(Obm, new Dictionary<string, string>(_github.Environment) { ["TMPDIR"] = temp }, "update", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output.Split(Environment.NewLine)[^1], Does.StartWith($"obm: couldn't download {Tarball}, so nothing was changed. "));
            Assert.That(output, Does.Not.Contain("Unhandled exception"));
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
            Assert.That(output, Is.EqualTo($"obm: GitHub doesn't give a SHA-256 hash for {Tarball}, so it can't be checked, and nothing was changed"));
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
                ", so it can't be updated there. Unpack the new version there yourself, or into a folder of yours, such as ~/.local/share/openbackupmanager"));
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

    // Such as behind a proxy whose certificate authority isn't trusted, rather than .NET's "see inner exception"
    [Test]
    public void Update_WhenTheCertificateIsntTrusted_SaysWhy()
    {
        using var server = new UntrustedServer();

        var (exitCode, output) = Tool.Run(Obm, new Dictionary<string, string> { ["OBM_UPDATE_SOURCE"] = server.Url }, "update", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Does.StartWith("obm: couldn't reach GitHub to check for updates. ").And.Contain("certificate"));
            Assert.That(output, Does.Not.Contain("inner exception"));
        }
    }

    // A local source is how a local development build is tried, so only GitHub is refused
    [Test]
    public void Update_OfABuildOfYourOwn_IsRefused()
    {
        Assume.That(Version, Is.EqualTo("0.0.0"), "a release's build");

        var (exitCode, output) = Tool.Run(Obm, "update", "--home", _home);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1), output);
            Assert.That(output, Is.EqualTo("obm: this is a local development build (0.0.0), so obm update won't replace it with a release"));
        }
    }

    // What it unpacks and checks it can write, which it removes
    private string[] Leftovers() => [.. Directory.GetFiles(_programs, ".*.update"), .. Directory.GetFiles(_programs, ".obm-update-*")];

    private (int ExitCode, string Output) Update(params string[] args) =>
        Tool.Run(Obm, _github.Environment, ["update", "--home", _home, .. args]);

    // Answers HTTPS with a certificate of its own, which nothing trusts
    private sealed class UntrustedServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate;

        public UntrustedServer()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            // macOS can't use a certificate's key for TLS until it's been through a PFX
            _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
            _listener.Start();
            _ = Task.Run(AnswerAsync);
        }

        public string Url => $"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public void Dispose()
        {
            _listener.Stop();
            _listener.Dispose();
            _certificate.Dispose();
        }

        private async Task AnswerAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    return;
                }

                using (client)
                using (var tls = new SslStream(client.GetStream()))
                {
                    try
                    {
                        await tls.AuthenticateAsServerAsync(_certificate);
                    }
                    // As the client refuses it
                    catch (Exception e) when (e is AuthenticationException or IOException)
                    {
                    }
                }
            }
        }
    }
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
