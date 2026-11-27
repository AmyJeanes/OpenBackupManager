using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;
using SetupQuestion = OpenBackupManager.Core.Protocol.SetupQuestion;

namespace OpenBackupManager.Engine.Tests;

// Through the engine's RPC, with rclone's alias backend, which asks one question and needs no network
public class SetupsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private AppPaths _home = null!;
    private IHost? _engine;

    private string RcloneFolder => Path.Combine(_home.State, "rclone");

    private string Target => Path.Combine(_home.State, "files");

    [SetUp]
    public void SetUp()
    {
        _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-setups-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(_home.Config);
        File.WriteAllText(_home.ConfigFile, "secret-store = \"file\"\n");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_engine is not null)
        {
            await _engine.StopAsync();
            _engine.Dispose();
            _engine = null;
        }

        TestFolders.Delete(_home.State);
    }

    [Test]
    public async Task ANewRemote_IsSetUp_ThenAddedToConfig_AndStarted()
    {
        await StartAsync();
        using var client = await ConnectAsync();

        var first = await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias", "NAS"), CancellationToken.None);
        var root = await client.AnswerSetupAsync("nas", Target, CancellationToken.None);
        var confirm = await client.AnswerSetupAsync("nas", "Backups", CancellationToken.None);
        var last = await client.AnswerSetupAsync("nas", "yes", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Question?.Name, Is.EqualTo("remote"));
            Assert.That(root.Question?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(confirm.Question?.Name, Is.EqualTo(SetupQuestion.RootConfirm));
            Assert.That(confirm.Question?.Folder, Is.EqualTo(new RootFolder("Backups/", RootFolderState.Missing, 0, 0)));
            Assert.That(last.Question, Is.Null);
            // Made by the check that it can hold files, which leaves nothing behind
            Assert.That(Directory.EnumerateFileSystemEntries(Path.Combine(Target, "Backups")), Is.Empty);
            Assert.That(
                await File.ReadAllTextAsync(_home.ConfigFile),
                Is.EqualTo("secret-store = \"file\"\n\n[[remotes]]\nid = \"nas\"\nname = \"NAS\"\nroot = \"Backups/\"\n"));
            Assert.That(RcloneConfigFile.IsEncrypted(Path.Combine(RcloneFolder, "nas.conf")), Is.True);
            Assert.That(Directory.GetFiles(RcloneFolder).Select(Path.GetFileName), Is.EqualTo(["nas.conf"]));
            Assert.That(await StateAsync(client, "nas", RemoteState.Running), Is.EqualTo(RemoteState.Running));
        }
    }

    // With no config, or one only a password that's gone could read, which setting it up replaces
    [TestCase(null)]
    [TestCase("RCLONE_ENCRYPT_V0:\nunreadable\n")]
    public async Task OneThatNeedsSettingUp_IsSetUpWhereItIs(string? conf)
    {
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\nroot = \"\"\n");
        if (conf is not null)
        {
            Directory.CreateDirectory(RcloneFolder);
            await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf"), conf);
        }

        await StartAsync();
        using var client = await ConnectAsync();
        await StateAsync(client, "nas", RemoteState.NeedsSetup);

        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias", "Ignored"), CancellationToken.None);
        var last = await client.AnswerSetupAsync("nas", Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // It keeps its root, so it's done once rclone's questions are
            Assert.That(last.Question, Is.Null);
            Assert.That(await StateAsync(client, "nas", RemoteState.Running), Is.EqualTo(RemoteState.Running));
            Assert.That(await File.ReadAllTextAsync(_home.ConfigFile), Is.EqualTo("secret-store = \"file\"\n\n[[remotes]]\nid = \"nas\"\nroot = \"\"\n"));
        }
    }

    // One rclone can't read, or one only a password that's gone could, which bringing back its store would
    [TestCase("not a config")]
    [TestCase("RCLONE_ENCRYPT_V0:\nunreadable\n")]
    public async Task Cancelling_LeavesWhatWasThere(string conf)
    {
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\nroot = \"\"\n");
        Directory.CreateDirectory(RcloneFolder);
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf"), conf);
        await StartAsync();
        using var client = await ConnectAsync();
        await StateAsync(client, "nas", RemoteState.NeedsSetup);

        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.CancelSetupAsync("nas", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(Path.Combine(RcloneFolder, "nas.conf")), Is.EqualTo(conf));
            Assert.That(Directory.GetFiles(RcloneFolder).Select(Path.GetFileName), Is.EqualTo(["nas.conf"]));
            // And it can be set up again
            Assert.That((await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None)).Question, Is.Not.Null);
        }
    }

    // Starting afresh loses what only the old password could read, so it's the person's choice
    [Test]
    public async Task WithoutTheConfigPassword_ItStartsAfresh_OnlyWhenAsked()
    {
        Directory.CreateDirectory(RcloneFolder);
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "work.conf"), "RCLONE_ENCRYPT_V0:\nunreadable\n");
        await StartAsync();
        using var client = await ConnectAsync();

        Assert.That(
            () => client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Property(nameof(EngineException.Problem)).EqualTo(EngineProblem.NoConfigPassword));
        var afresh = await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias", StartAfresh: true), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afresh.Question, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(RcloneFolder, "work.conf")), Is.False);
        }
    }

    // Such as one antivirus has open, which the next start can clear
    [Test]
    public async Task Starting_CarriesOn_PastWhatItCantClear()
    {
        Directory.CreateDirectory(RcloneFolder);
        var left = Path.Combine(RcloneFolder, "nas.conf.setup");
        await File.WriteAllTextAsync(left, "");
        // Windows won't delete an open file, and elsewhere one can't be deleted from a folder that can't be written
        using var held = OperatingSystem.IsWindows() ? new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(RcloneFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            await StartAsync();
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(RcloneFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Assert.That(File.Exists(left), Is.True);
    }

    // As another user's link could lead it to delete files elsewhere, or a socket there name a process of this user's
    // for it to stop
    [Test]
    [Platform(Exclude = "Win")]
    public async Task ARunFolderThatsALink_IsRefused_BeforeAnythingIsCleared()
    {
        var elsewhere = Path.Combine(_home.State, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        await File.WriteAllTextAsync(Path.Combine(elsewhere, "backends.sock"), "");
        Directory.CreateSymbolicLink(RunFolder.Path(_home), elsewhere);

        Assert.That(() => StartAsync(), Throws.TypeOf<EngineException>().With.Message.Contains("is a link"));
        Assert.That(File.Exists(Path.Combine(elsewhere, "backends.sock")), Is.True);
    }

    // Left by an engine that ended in the middle of a setup, such as by crashing, which on macOS leaves its rclone
    // running
    [Test]
    public async Task Starting_ClearsWhatASetupLeft()
    {
        Directory.CreateDirectory(RcloneFolder);
        foreach (var name in new[] { "nas.conf", "nas.conf.setup", "nas.conf.old", "nas.conf.setup123456" })
        {
            await File.WriteAllTextAsync(Path.Combine(RcloneFolder, name), "[remote]\ntype = local\n");
        }

        RunFolder.Create(_home);
        var config = Path.Combine(_home.State, "left.conf");
        await File.WriteAllTextAsync(config, "");
        await using var left = await Rcd.StartAsync(
            new RcdOptions(
                BundledRclone.FilePath,
                RunFolder.Socket(_home, "su-12345678.sock"),
                config,
                Path.Combine(_home.State, "left-cache"),
                Path.Combine(_home.State, "left.log"),
                "OpenBackupManager-Tests"),
            CancellationToken.None);

        await StartAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(RcloneFolder).Select(Path.GetFileName), Is.EqualTo(["nas.conf"]));
            Assert.That(() => left.Exited.WaitAsync(Timeout), Throws.Nothing);
        }
    }

    // As rclone saves a config by moving it aside first, so if it was stopped before putting the new one in its place,
    // that's the only copy
    [Test]
    public async Task Starting_PutsBackAConfigRcloneWasStoppedSaving()
    {
        Directory.CreateDirectory(RcloneFolder);
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf.old123456789"), "[remote]\ntype = local\n");
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf987654321"), "[remote]\n");
        // Moved aside from a setup's copy, so deleted rather than put back
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf.setup.old123456789"), "[remote]\n");

        await StartAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(RcloneFolder).Select(Path.GetFileName), Is.EqualTo(["nas.conf"]));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(RcloneFolder, "nas.conf")), Is.EqualTo("[remote]\ntype = local\n"));
        }
    }

    [Test]
    public async Task WhenConfigTomlCantTakeIt_ItIsntSetUp()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", Target, CancellationToken.None);
        await client.AnswerSetupAsync("nas", "", CancellationToken.None);
        await File.WriteAllTextAsync(_home.ConfigFile, "secret-store = \"file\"\nversion = \"one\"\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => client.AnswerSetupAsync("nas", "yes", CancellationToken.None),
                Throws.TypeOf<EngineException>().With.Message.Match(
                    "^nas wasn't set up, as adding it to config.toml failed: config.toml can't be changed until it's fixed: .+[^.]\\. Once that's fixed, set it up again$"));
            Assert.That(Directory.GetFiles(RcloneFolder), Is.Empty);
        }
    }

    [Test]
    public async Task TheRoot_TakesTheCaseOfTheFoldersThatExist_WhereTheRemoteIgnoresCase()
    {
        Directory.CreateDirectory(Path.Combine(Target, "Work", "Archive"));
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", Target, CancellationToken.None);

        await client.AnswerSetupAsync("nas", "work/archive/new", CancellationToken.None);
        await client.AnswerSetupAsync("nas", "yes", CancellationToken.None);

        Assert.That(
            await File.ReadAllTextAsync(_home.ConfigFile),
            Does.EndWith(OperatingSystem.IsLinux() ? "root = \"work/archive/new/\"\n" : "root = \"Work/Archive/new/\"\n"));
    }

    // local's remote is the whole filesystem, so its root is a full path from a drive or /
    [Test]
    public async Task AFullPath_KeepsItsDriveOrSlash()
    {
        Directory.CreateDirectory(Target);
        var full = Target.Replace('\\', '/');
        using var client = await LocalAsync();

        await client.AnswerSetupAsync("nas", full, CancellationToken.None);
        await client.AnswerSetupAsync("nas", "yes", CancellationToken.None);

        Assert.That(await File.ReadAllTextAsync(_home.ConfigFile), Does.EndWith($"root = \"{full}/\"\n"));
    }

    // As anything else is from the folder the engine started in, not the same for a service, the app and a shell
    [TestCase("Backups")]
    [TestCase("")]
    public async Task ALocalRootThatIsntAFullPath_IsAskedAgain(string root) => await AskedAgainForAFullPathAsync(root);

    // From the drive, or the folder on the drive, the engine started in. A server with no share would be one there too
    [Platform("Win")]
    [TestCase(@"\Backups")]
    [TestCase("C:Backups")]
    [TestCase(@"\\nas")]
    [TestCase(@"\\nas\")]
    public async Task ALocalRootFromNoDrive_IsAskedAgain(string root) => await AskedAgainForAFullPathAsync(root);

    // As Windows writes them, from a drive or a share, kept as given apart from the case
    [Test]
    [Platform("Win")]
    public async Task AWindowsPath_IsTakenApartAtItsBackslashes([Values] bool share)
    {
        Directory.CreateDirectory(Path.Combine(Target, "Work"));
        var full = share ? $@"\\localhost\{Target[0]}${Target[2..]}" : Target;
        using var client = await LocalAsync();

        var confirm = await client.AnswerSetupAsync("nas", full + @"\work", CancellationToken.None);

        Assert.That(confirm.Question?.Folder, Is.EqualTo(new RootFolder(full + @"\Work\", RootFolderState.Empty, 0, 0)));
    }

    // As rclone would take them as steps, not folders
    [TestCase("Work/./Backups")]
    [TestCase("Work/../Backups")]
    public async Task APathWithDots_IsAskedAgain(string path)
    {
        using var client = await SignedInAsync();

        var again = await client.AnswerSetupAsync("nas", path, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Question?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(again.Question?.Error, Is.EqualTo("Give the path without . or .."));
        }
    }

    [Test]
    public async Task ARootThatCantBeLookedAt_IsAskedAgain_WithWhy()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", "nowhere:files", CancellationToken.None);

        var again = await client.AnswerSetupAsync("nas", "Backups", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Question?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(again.Question?.Error, Does.StartWith("rclone's operations/fsinfo failed:"));
        }
    }

    // As an S3 key kept to one bucket can't list the top: here an http server that refuses to
    [Test]
    public async Task ATopThatCantBeListed_IsPassedOver()
    {
        await using var server = FakeHttp.OnPort(path => path switch
        {
            "" => (403, ""),
            "Backups/" => (200, "<a href=\"notes.txt\">notes.txt</a>"),
            "Backups/notes.txt" => (200, ""),
            _ => (404, ""),
        });
        await StartAsync();
        using var client = await ConnectAsync();
        var step = await client.SetupRemoteAsync(
            new SetupRemoteParams("nas", "http", Parameters: new Dictionary<string, string> { ["url"] = server.Url! }),
            CancellationToken.None);
        while (step.Question is { Name: not SetupQuestion.Root } question)
        {
            step = await client.AnswerSetupAsync("nas", question.Default, CancellationToken.None);
        }

        var confirm = await client.AnswerSetupAsync("nas", "Backups", CancellationToken.None);

        Assert.That(confirm.Question?.Folder, Is.EqualTo(new RootFolder("Backups/", RootFolderState.HasFiles, 0, 1, AboveUnlisted: true)));
    }

    // As local lists a folder it can't open as empty
    [Test]
    public async Task AFolderAboveThatCantBeListed_IsPassedOver()
    {
        var locked = Path.Combine(Target, "Locked");
        Directory.CreateDirectory(Path.Combine(locked, "Backups"));
        await File.WriteAllTextAsync(Path.Combine(locked, "Backups", "notes.txt"), "");
        using var client = await SignedInAsync();
        SetListable(locked, false);
        SetupStep confirm;
        try
        {
            confirm = await client.AnswerSetupAsync("nas", "Locked/Backups", CancellationToken.None);
        }
        finally
        {
            SetListable(locked, true);
        }

        Assert.That(confirm.Question?.Folder, Is.EqualTo(new RootFolder("Locked/Backups/", RootFolderState.HasFiles, 0, 1, AboveUnlisted: true)));
    }

    [Test]
    public async Task Folders_AreListed_InTheRemotesOwnCase_WithoutAnsweringTheRoot()
    {
        Directory.CreateDirectory(Path.Combine(Target, "Work", "Backups"));
        Directory.CreateDirectory(Path.Combine(Target, "Work", "Archive"));
        await File.WriteAllTextAsync(Path.Combine(Target, "Work", "notes.txt"), "");
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", Target, CancellationToken.None);

        var top = await client.ListFoldersAsync("nas", "", CancellationToken.None);
        var work = await client.ListFoldersAsync("nas", "Work", CancellationToken.None);
        await client.AnswerSetupAsync("nas", "Work/Backups", CancellationToken.None);
        var last = await client.AnswerSetupAsync("nas", "yes", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(top.Path, Is.Empty);
            Assert.That(top.Folders, Is.EqualTo<string[]>(["Work"]));
            Assert.That(work.Path, Is.EqualTo("Work/"));
            Assert.That(work.Folders, Is.EqualTo<string[]>(["Archive", "Backups"]));
            Assert.That(last.Question, Is.Null);
        }
    }

    [Test]
    public async Task ListingAFolderThatIsntThere_SaysSo_AndTheSetupCarriesOn()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => client.ListFoldersAsync("nas", "Nowhere", CancellationToken.None),
                Throws.TypeOf<EngineException>().With.Message.EqualTo("There's no folder Nowhere/ on nas"));
            Assert.That((await client.AnswerSetupAsync("nas", "", CancellationToken.None)).Question?.Name, Is.EqualTo(SetupQuestion.RootConfirm));
        }
    }

    [TestCase("Work", RootFolderState.HasFiles, 2, 1, "no")]
    [TestCase("Empty", RootFolderState.Empty, 0, 0, "yes")]
    public async Task TheConfirm_SaysWhatsThere(string root, RootFolderState state, int folders, int files, string answer)
    {
        Directory.CreateDirectory(Path.Combine(Target, "Work", "Archive"));
        Directory.CreateDirectory(Path.Combine(Target, "Work", "Backups"));
        await File.WriteAllTextAsync(Path.Combine(Target, "Work", "notes.txt"), "");
        Directory.CreateDirectory(Path.Combine(Target, "Empty"));
        using var client = await SignedInAsync();

        var confirm = (await client.AnswerSetupAsync("nas", root, CancellationToken.None)).Question;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(confirm?.Folder, Is.EqualTo(new RootFolder(root + "/", state, folders, files)));
            Assert.That(confirm?.Default, Is.EqualTo(answer));
        }
    }

    [Test]
    public async Task AFolderThatIsntThere_PointsToOneItMayHaveMeant()
    {
        Directory.CreateDirectory(Path.Combine(Target, "Work", "Backups"));
        using var client = await SignedInAsync();

        var confirm = (await client.AnswerSetupAsync("nas", "Work/Bakups", CancellationToken.None)).Question;

        Assert.That(confirm?.Folder, Is.EqualTo(new RootFolder("Work/Bakups/", RootFolderState.Missing, 0, 0, "Work/Backups/")));
    }

    [Test]
    public async Task AFolderThatIsntThere_PointsToTheNearest()
    {
        Directory.CreateDirectory(Path.Combine(Target, "Phones"));
        Directory.CreateDirectory(Path.Combine(Target, "Photo"));
        using var client = await SignedInAsync();

        var confirm = (await client.AnswerSetupAsync("nas", "Photos", CancellationToken.None)).Question;

        Assert.That(confirm?.Folder?.NearMiss, Is.EqualTo("Photo/"));
    }

    [Test]
    public async Task AShortFolderThatIsntThere_PointsToNone()
    {
        Directory.CreateDirectory(Path.Combine(Target, "TV"));
        using var client = await SignedInAsync();

        var confirm = (await client.AnswerSetupAsync("nas", "PC", CancellationToken.None)).Question;

        Assert.That(confirm?.Folder?.NearMiss, Is.Null);
    }

    [Test]
    public async Task No_AsksForTheRootAgain()
    {
        using var client = await SignedInAsync();
        await client.AnswerSetupAsync("nas", "Backups", CancellationToken.None);

        var again = (await client.AnswerSetupAsync("nas", "no", CancellationToken.None)).Question;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(again?.Error, Is.Null);
            Assert.That(Directory.Exists(Path.Combine(Target, "Backups")), Is.False);
        }
    }

    [TestCase("Work", "There's already an Open Backup Manager backup in Work/. Joining is not yet available")]
    [TestCase("Work/PC Backup", "There's an Open Backup Manager backup in Work/, which Work/PC Backup/ is inside. Joining is not yet available")]
    public async Task ABackupsFolder_OrOneInsideIt_IsRefused(string root, string why)
    {
        Directory.CreateDirectory(Path.Combine(Target, "Work", ".obm"));
        Directory.CreateDirectory(Path.Combine(Target, "Work", "PC Backup"));
        using var client = await SignedInAsync();

        var again = (await client.AnswerSetupAsync("nas", root, CancellationToken.None)).Question;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(again?.Error, Is.EqualTo(why));
        }
    }

    [Test]
    public async Task AFolderThatCantHoldFiles_AsksForTheRootAgain_WithWhy()
    {
        // A file where its folder would be
        Directory.CreateDirectory(Target);
        await File.WriteAllTextAsync(Path.Combine(Target, "Blocked"), "");
        using var client = await SignedInAsync();
        await client.AnswerSetupAsync("nas", "Blocked/Inside", CancellationToken.None);

        var again = (await client.AnswerSetupAsync("nas", "yes", CancellationToken.None)).Question;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(again?.Error, Does.StartWith("Blocked/Inside/ can't hold files: rclone's operations/mkdir failed:"));
        }
    }

    [Test]
    public async Task OneSetUpAgain_WhoseRootCantHoldFiles_IsntSetUp()
    {
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\nroot = \"Blocked/\"\n");
        Directory.CreateDirectory(Target);
        await File.WriteAllTextAsync(Path.Combine(Target, "Blocked"), "");
        await StartAsync();
        using var client = await ConnectAsync();
        await StateAsync(client, "nas", RemoteState.NeedsSetup);
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => client.AnswerSetupAsync("nas", Target, CancellationToken.None),
                Throws.TypeOf<EngineException>().With.Message.StartsWith("nas couldn't be set up, as Blocked/ can't hold files:"));
            Assert.That(File.Exists(Path.Combine(RcloneFolder, "nas.conf")), Is.False);
        }
    }

    [Test]
    public async Task Folders_ArentListed_BeforeItsSignedIn()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);

        Assert.That(
            () => client.ListFoldersAsync("nas", "", CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.EqualTo("nas isn't signed in yet, so its folders can't be listed"));
    }

    [Test]
    public async Task AnsweringOneThatsEnded_SaysSo()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.CancelSetupAsync("nas", CancellationToken.None);

        Assert.That(
            () => client.AnswerSetupAsync("nas", Target, CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.EqualTo(
                "There's no setup of nas waiting for an answer here. It may have finished or been cancelled, or another program may have started it"));
    }

    [Test]
    public async Task ClosingTheConnection_EndsIt()
    {
        await StartAsync();
        using (var client = await ConnectAsync())
        {
            await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        }

        using var another = await ConnectAsync();
        var again = await SetUpAgainAsync(another, "nas");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Question, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(RcloneFolder, "nas.conf")), Is.False);
        }
    }

    // Such as a sign-in waiting for a browser, here a server that never answers
    [Test]
    public async Task ClosingTheConnection_DuringALongCall_EndsIt()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        await StartAsync();
        var client = await ConnectAsync();
        var step = await client.SetupRemoteAsync(
            new SetupRemoteParams("nas", "http", Parameters: new Dictionary<string, string> { ["url"] = url }),
            CancellationToken.None);
        while (step.Question is { Name: not SetupQuestion.Root } question)
        {
            step = await client.AnswerSetupAsync("nas", question.Default, CancellationToken.None);
        }

        var answering = client.AnswerSetupAsync("nas", "Backups", CancellationToken.None);
        using var asked = await server.AcceptTcpClientAsync().WaitAsync(Timeout);
        client.Dispose();

        Assert.That(async () => await answering, Throws.TypeOf<EngineException>());
        using var another = await ConnectAsync();
        Assert.That((await SetUpAgainAsync(another, "nas")).Question, Is.Not.Null);
    }

    [Test]
    public async Task OneBeingSetUp_IsRefused_ElsewhereToo()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        using var another = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);

        Assert.That(
            () => another.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.EqualTo("nas is already being set up"));
    }

    [Test]
    public async Task OneAlreadySetUp_IsRefused()
    {
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\nroot = \"\"\n");
        Directory.CreateDirectory(RcloneFolder);
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf"), "[remote]\ntype = local\n");
        await StartAsync();
        using var client = await ConnectAsync();
        await StateAsync(client, "nas", RemoteState.Running);

        Assert.That(
            () => client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.EqualTo("nas is already set up. To set it up again, take it out of config.toml first"));
    }

    [TestCase("NAS")]
    [TestCase("../nas")]
    [TestCase("con")]
    public async Task AnIdThatCantNameItsFiles_IsRefused(string id)
    {
        await StartAsync();
        using var client = await ConnectAsync();

        Assert.That(
            () => client.SetupRemoteAsync(new SetupRemoteParams(id, "alias"), CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.StartsWith($"It can't be set up, as the remote id \"{id}\" can only use"));
        Assert.That(Directory.Exists(RcloneFolder), Is.False);
    }

    [Test]
    public async Task AStepThatFails_EndsIt()
    {
        await StartAsync();
        using var client = await ConnectAsync();

        Assert.That(
            () => client.SetupRemoteAsync(new SetupRemoteParams("nas", "no-such-backend"), CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.StartsWith("nas couldn't be set up: rclone's config/create failed"));
        Assert.That((await TrySetupAsync(client, "nas")).Question, Is.Not.Null);
    }

    private async Task StartAsync()
    {
        _engine = EngineHost.Build(_home);
        await _engine.StartAsync();
    }

    // At the root question, through alias to Target
    private async Task<EngineClient> SignedInAsync()
    {
        await StartAsync();
        var client = await ConnectAsync();
        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", Target, CancellationToken.None);
        return client;
    }

    private async Task AskedAgainForAFullPathAsync(string root)
    {
        using var client = await LocalAsync();

        var again = await client.AnswerSetupAsync("nas", root, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Question?.Name, Is.EqualTo(SetupQuestion.Root));
            Assert.That(again.Question?.Error, Does.StartWith("Give the full path, from "));
        }
    }

    // At the root question, through local, answering its own questions, such as whether to use UNC paths on Windows,
    // as they come
    private async Task<EngineClient> LocalAsync()
    {
        await StartAsync();
        var client = await ConnectAsync();
        var step = await client.SetupRemoteAsync(new SetupRemoteParams("nas", "local"), CancellationToken.None);
        while (step.Question is { Name: not SetupQuestion.Root } question)
        {
            step = await client.AnswerSetupAsync("nas", question.Default, CancellationToken.None);
        }

        return client;
    }

    private async Task<EngineClient> ConnectAsync() =>
        await EngineClient.ConnectAsync(_home, Timeout, CancellationToken.None) ?? throw new AssertionException("The engine isn't running");

    // Refused while the last one is still ending
    private static async Task<SetupStep> TrySetupAsync(EngineClient client, string id)
    {
        try
        {
            return await client.SetupRemoteAsync(new SetupRemoteParams(id, "alias"), CancellationToken.None);
        }
        catch (EngineException)
        {
            return new SetupStep();
        }
    }

    // Listable or only to be passed through, into what's in it
    private static void SetListable(string folder, bool listable)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new DirectoryInfo(folder);
            var acl = info.GetAccessControl();
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
            if (listable)
            {
                acl.RemoveAccessRule(rule);
            }
            else
            {
                acl.AddAccessRule(rule);
            }

            info.SetAccessControl(acl);
        }
        else
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserWrite | UnixFileMode.UserExecute | (listable ? UnixFileMode.UserRead : 0));
        }
    }

    // Once the engine has seen the last one end, or refused after the timeout
    private static async Task<SetupStep> SetUpAgainAsync(EngineClient client, string id)
    {
        var timeout = DateTime.UtcNow + Timeout;
        var again = await TrySetupAsync(client, id);
        while (again.Question is null && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100);
            again = await TrySetupAsync(client, id);
        }

        return again;
    }

    // Once it's in that state, or what it was in after the timeout
    private static async Task<RemoteState?> StateAsync(EngineClient client, string id, RemoteState state)
    {
        var timeout = DateTime.UtcNow + Timeout;
        RemoteState? now;
        do
        {
            now = (await client.StatusAsync(CancellationToken.None)).Remotes.FirstOrDefault(remote => remote.Id == id)?.State;
            if (now == state)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < timeout);

        return now;
    }
}
