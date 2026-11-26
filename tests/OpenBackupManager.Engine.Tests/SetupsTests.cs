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

        // On Windows, something else, such as an antivirus scan, can hold rclone's log for a moment after it exits
        for (var tries = 1; ; tries++)
        {
            try
            {
                Directory.Delete(_home.State, recursive: true);
                return;
            }
            catch (IOException) when (tries < 20)
            {
                Thread.Sleep(100);
            }
        }
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

    [Test]
    public async Task OneThatNeedsSettingUp_IsSetUpWhereItIs()
    {
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\nroot = \"\"\n");
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

    [Test]
    public async Task Cancelling_LeavesWhatWasThere()
    {
        // One rclone can't read
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\nroot = \"\"\n");
        Directory.CreateDirectory(RcloneFolder);
        await File.WriteAllTextAsync(Path.Combine(RcloneFolder, "nas.conf"), "not a config");
        await StartAsync();
        using var client = await ConnectAsync();
        await StateAsync(client, "nas", RemoteState.NeedsSetup);

        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None);
        await client.CancelSetupAsync("nas", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(Path.Combine(RcloneFolder, "nas.conf")), Is.EqualTo("not a config"));
            Assert.That(Directory.GetFiles(RcloneFolder).Select(Path.GetFileName), Is.EqualTo(["nas.conf"]));
            // And it can be set up again
            Assert.That((await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias"), CancellationToken.None)).Question, Is.Not.Null);
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
        await StartAsync();
        using var client = await ConnectAsync();

        // Such as whether to use UNC paths, on Windows
        var step = await client.SetupRemoteAsync(new SetupRemoteParams("nas", "local"), CancellationToken.None);
        while (step.Question is { Name: not SetupQuestion.Root } question)
        {
            step = await client.AnswerSetupAsync("nas", question.Default, CancellationToken.None);
        }

        await client.AnswerSetupAsync("nas", full, CancellationToken.None);
        await client.AnswerSetupAsync("nas", "yes", CancellationToken.None);

        Assert.That(await File.ReadAllTextAsync(_home.ConfigFile), Does.EndWith($"root = \"{full}/\"\n"));
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
        // Once the engine has seen it close
        var timeout = DateTime.UtcNow + Timeout;
        var again = await TrySetupAsync(another, "nas");
        while (again.Question is null && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100);
            again = await TrySetupAsync(another, "nas");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Question, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(RcloneFolder, "nas.conf")), Is.False);
        }
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
