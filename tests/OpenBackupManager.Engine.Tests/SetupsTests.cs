using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

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

        var first = await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias", "NAS", "/"), CancellationToken.None);
        var last = await client.AnswerSetupAsync("nas", Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Question?.Name, Is.EqualTo("remote"));
            Assert.That(last.Question, Is.Null);
            Assert.That(
                await File.ReadAllTextAsync(_home.ConfigFile),
                Is.EqualTo("secret-store = \"file\"\n\n[[remotes]]\nid = \"nas\"\nname = \"NAS\"\nroot = \"/\"\n"));
            Assert.That(RcloneConfigFile.IsEncrypted(Path.Combine(RcloneFolder, "nas.conf")), Is.True);
            Assert.That(Directory.GetFiles(RcloneFolder).Select(Path.GetFileName), Is.EqualTo(["nas.conf"]));
            Assert.That(await StateAsync(client, "nas", RemoteState.Running), Is.EqualTo(RemoteState.Running));
        }
    }

    [Test]
    public async Task OneThatNeedsSettingUp_IsSetUpWhereItIs()
    {
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\n");
        await StartAsync();
        using var client = await ConnectAsync();
        await StateAsync(client, "nas", RemoteState.NeedsSetup);

        await client.SetupRemoteAsync(new SetupRemoteParams("nas", "alias", "Ignored"), CancellationToken.None);
        await client.AnswerSetupAsync("nas", Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await StateAsync(client, "nas", RemoteState.Running), Is.EqualTo(RemoteState.Running));
            Assert.That(await File.ReadAllTextAsync(_home.ConfigFile), Is.EqualTo("secret-store = \"file\"\n\n[[remotes]]\nid = \"nas\"\n"));
        }
    }

    [Test]
    public async Task Cancelling_LeavesWhatWasThere()
    {
        // One rclone can't read
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\n");
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
        await File.WriteAllTextAsync(_home.ConfigFile, "secret-store = \"file\"\nversion = \"one\"\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => client.AnswerSetupAsync("nas", Target, CancellationToken.None),
                Throws.TypeOf<EngineException>().With.Message.Match(
                    "^nas wasn't set up, as adding it to config.toml failed: config.toml can't be changed until it's fixed: .+[^.]\\. Once that's fixed, set it up again$"));
            Assert.That(Directory.GetFiles(RcloneFolder), Is.Empty);
        }
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
        await File.AppendAllTextAsync(_home.ConfigFile, "\n[[remotes]]\nid = \"nas\"\n");
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
