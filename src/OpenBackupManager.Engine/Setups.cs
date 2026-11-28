using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;
using SetupChoice = OpenBackupManager.Core.Protocol.SetupChoice;
using SetupQuestion = OpenBackupManager.Core.Protocol.SetupQuestion;

namespace OpenBackupManager.Engine;

// Sets remotes up through rclone's own questions, each with an rcd of its own while it lasts. It writes a copy of the
// remote's config, which replaces its own only once it's set up, so one stopped part way, or one being set up again,
// leaves what was there. A new one is added to config.toml last, which starts its own rcd
internal sealed partial class Setups(AppPaths paths, ConfigWatcher config, RcloneConfig rcloneConfig, Remotes remotes, ILogger<Setups> logger)
{
    // The account's section in the remote's config, as crypt over it would be [crypt]
    internal const string Section = "remote";

    private readonly HashSet<string> _settingUp = [];

    // Its first question is in Question, which is null when nothing needed asking
    public async Task<Setup> StartAsync(SetupRemoteParams start, CancellationToken cancellationToken)
    {
        var id = start.Id;
        if (!RemoteSettings.IsValidId(id))
        {
            throw new EngineException("It can't be set up, as " + new ConfigProblem(0, ConfigProblemKind.RemoteIdNotValid, IsError: true, id).Message(CultureInfo.InvariantCulture));
        }

        if (remotes.Status().FirstOrDefault(remote => remote.Id == id) is { State: not RemoteState.NeedsSetup })
        {
            throw new EngineException($"{id} is already set up. To set it up again, take it out of config.toml first");
        }

        // Or it couldn't be added once it's set up
        if (config.Result.HasErrors)
        {
            throw new EngineException($"config.toml has to be fixed first: {config.Result.Problems.First(p => p.IsError)}");
        }

        lock (_settingUp)
        {
            if (!_settingUp.Add(id))
            {
                throw new EngineException($"{id} is already being set up");
            }
        }

        var options = remotes.Options(id, setup: true);
        Rcd? rcd = null;
        Setup? setup = null;
        try
        {
            var password = await RcloneAsync(id, () => rcloneConfig.PasswordForSetupAsync(id, start.StartAfresh, cancellationToken));
            rcd = await RcloneAsync(id, async () =>
            {
                RunFolder.Create(paths);
                PrivateFiles.CreateFolder(Path.GetDirectoryName(options.Config)!);
                PrivateFiles.CreateFolder(Path.GetDirectoryName(options.LogFile)!);
                // Left by an engine that stopped in the middle of one
                File.Delete(options.Config);
                var started = await Rcd.StartAsync(options, cancellationToken);
                try
                {
                    // Before anything's written, as without it rclone writes the config in plain text
                    await started.Client.UnlockConfigAsync(password.Reveal(), cancellationToken);
                }
                catch
                {
                    await started.DisposeAsync();
                    throw;
                }

                return started;
            });

            LogSettingUp(id, start.Type);
            // Ours, in order. One set up again keeps its root
            var folders = new RemoteFolders(rcd.Client, Section + ":", fullPathsOnly: start.Type == "local");
            var root = config.Config.Remotes.FirstOrDefault(remote => remote.Id == id)?.Root;
            var choices = new SetupChoices { Root = root };
            OwnQuestion[] own = root is not null ? [] : [new RootQuestion(folders, choices), new RootConfirmQuestion(folders, choices)];
            var preset = Preset.For(start.Type);
            setup = new Setup(
                this,
                start,
                new RemoteSetup(rcd.Client, Section, Preset.Answer(preset, start.Parameters)),
                preset,
                own,
                choices,
                folders,
                rcd,
                options.Config);
            await setup.StepAsync((remoteSetup, ending) => remoteSetup.StartAsync(start.Type, ending), cancellationToken);
            return setup;
        }
        catch
        {
            if (setup is not null)
            {
                await setup.DisposeAsync();
            }
            else if (rcd is not null)
            {
                await EndAsync(id, rcd, options.Config);
            }
            else
            {
                Release(id);
            }

            throw;
        }
    }

    // A step's failure, as rclone's and the engine's own both say what's wrong
    internal static async Task<T> RcloneAsync<T>(string id, Func<Task<T>> step)
    {
        try
        {
            return await step();
        }
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException or UnauthorizedAccessException or Win32Exception
            or InvalidOperationException)
        {
            throw new EngineException($"{id} couldn't be set up: {e.Message}", e);
        }
    }

    // Once the copy is in place, so the rcd config.toml starts finds it
    internal async Task FinishAsync(Setup setup, Rcd rcd, string copy)
    {
        var id = setup.Id;
        try
        {
            // So nothing writes it once it's moved
            await rcd.DisposeAsync();
            var file = rcloneConfig.FileFor(id);
            await RcloneAsync(id, () =>
            {
                File.Move(copy, file, overwrite: true);
                return Task.FromResult(true);
            });

            if (config.Config.Remotes.Any(remote => remote.Id == id))
            {
                remotes.Retry(id);
            }
            else
            {
                try
                {
                    config.Add("remotes", [("id", id), ("name", setup.Name), ("root", setup.Root)]);
                }
                catch (Exception e) when (e is ConfigException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    File.Delete(file);
                    throw new EngineException($"{id} wasn't set up, as adding it to config.toml failed: {e.Message.TrimEnd('.')}. Once that's fixed, set it up again", e);
                }
            }

            LogSetUp(id);
        }
        finally
        {
            Release(id);
        }
    }

    // Leaving the remote's own config as it was
    internal async Task EndAsync(string id, Rcd rcd, string copy)
    {
        try
        {
            await rcd.DisposeAsync();
            File.Delete(copy);
        }
        // Deleted as the next setup of it starts
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            Release(id);
        }
    }

    private void Release(string id)
    {
        lock (_settingUp)
        {
            _settingUp.Remove(id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Setting up remote {Id}, of type {Type}")]
    private partial void LogSettingUp(string id, string type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Set up remote {Id}")]
    private partial void LogSetUp(string id);
}

// One remote's setup, which belongs to the connection that started it, and ends with it: rclone's questions, then
// our own
internal sealed class Setup(
    Setups setups,
    SetupRemoteParams start,
    RemoteSetup remoteSetup,
    Preset? preset,
    IReadOnlyList<OwnQuestion> own,
    SetupChoices choices,
    RemoteFolders folders,
    Rcd rcd,
    string copy) : IAsyncDisposable
{
    // How often it looks whether rclone's step is waiting for a sign-in, as rclone doesn't say
    private static readonly TimeSpan SignInCheck = TimeSpan.FromMilliseconds(100);

    // rclone's, for a sign-in pasted from rclone authorize on another device
    private const string PastedSignIn = "config_token";

    // Cancelled as it ends, as rclone's step can outlast the client's call, such as one waiting for a sign-in
    private readonly CancellationTokenSource _ending = new();

    private bool _ended;

    // rclone's latest, which may still be going
    private Task<Rclone.SetupQuestion?>? _step;

    // Once rclone's questions are done, as only then can it reach the remote
    private bool _signedIn;

    // Which of our own it's at
    private int _own;

    public string Id => start.Id;

    public string? Name => start.Name;

    public string? Root => choices.Root;

    // Null once it's set up
    public SetupQuestion? Question { get; private set; }

    public Task AnswerAsync(string answer, CancellationToken cancellationToken) =>
        Question?.Name == SetupQuestion.SignIn ? SignedInAsync(cancellationToken)
        : _signedIn ? AnswerOwnAsync(answer, cancellationToken)
        : AnswerRcloneAsync(answer, cancellationToken);

    // Without changing where the setup is, so a client can look around before it answers
    public async Task<FoundFolder> ListFoldersAsync(string path, CancellationToken cancellationToken)
    {
        if (!_signedIn)
        {
            throw new EngineException($"{Id} isn't signed in yet, so its folders can't be listed");
        }

        FoundFolder found;
        try
        {
            found = await folders.FindAsync(path, cancellationToken);
        }
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException or ArgumentException)
        {
            throw new EngineException($"{Id}'s folders couldn't be listed: {e.Message}", e);
        }

        return found.Folders is null ? throw new EngineException($"There's no folder {found.Path} on {Id}") : found;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_ended)
        {
            _ended = true;
            if (_step is { IsCompleted: false } step)
            {
                await StopAsync(step);
            }

            await setups.EndAsync(Id, rcd, copy);
        }

        _ending.Dispose();
    }

    // A blank sign-in is asked for again, as rclone would take it as nothing to change and carry on without one
    private Task AnswerRcloneAsync(string answer, CancellationToken cancellationToken)
    {
        if (Question is { Name: PastedSignIn } question && string.IsNullOrWhiteSpace(answer))
        {
            Question = question with { Error = "Paste what rclone authorize printed" };
            return Task.CompletedTask;
        }

        return StepAsync((remoteSetup, ending) => remoteSetup.AnswerAsync(answer, ending), cancellationToken);
    }

    internal Task StepAsync(Func<RemoteSetup, CancellationToken, Task<Rclone.SetupQuestion?>> step, CancellationToken cancellationToken)
    {
        _step = Setups.RcloneAsync(Id, () => step(remoteSetup, _ending.Token));
        return AskedAsync(cancellationToken);
    }

    // Once rclone's asked its next question, or is waiting for a sign-in, which is asked as our own, with its link
    private async Task AskedAsync(CancellationToken cancellationToken)
    {
        var step = _step!;
        while (await Task.WhenAny(step, Task.Delay(SignInCheck, cancellationToken)) != step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await Setups.RcloneAsync(Id, () => rcd.Client.OAuthStatusAsync(cancellationToken))).AuthUrl is { } link)
            {
                Question = new SetupQuestion(SetupQuestion.SignIn, "", "", [], Required: false, IsPassword: false, Exclusive: false, "string", Link: new Uri(link));
                return;
            }
        }

        await NextAsync(await step, cancellationToken);
    }

    private async Task SignedInAsync(CancellationToken cancellationToken) =>
        await NextAsync(await _step!.WaitAsync(cancellationToken), cancellationToken);

    private async Task NextAsync(Rclone.SetupQuestion? asked, CancellationToken cancellationToken)
    {
        if (asked is not null)
        {
            var question = new SetupQuestion(
                asked.Name,
                asked.Help,
                asked.Default,
                [.. asked.Choices.Select(choice => new SetupChoice(choice.Value, choice.Help))],
                asked.Required,
                asked.IsPassword,
                asked.Exclusive,
                asked.Type,
                asked.Error);
            Question = preset?.Word(question) ?? question;
            return;
        }

        _signedIn = true;
        await AskOwnAsync(null, cancellationToken);
    }

    // Now rather than as its rcd stops, which frees rclone's sign-in port for the next, and seen to end, or the engine
    // would log its failure as one nothing saw
    private async Task StopAsync(Task step)
    {
        try
        {
            if ((await rcd.Client.OAuthStatusAsync(CancellationToken.None)).AuthUrl is not null)
            {
                await rcd.Client.OAuthStopAsync(CancellationToken.None);
            }
        }
        // Its rcd has gone, and the step with it
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException)
        {
        }

        await _ending.CancelAsync();
        try
        {
            await step;
        }
        catch (Exception e) when (e is EngineException or OperationCanceledException)
        {
        }
    }

    private async Task AnswerOwnAsync(string answer, CancellationToken cancellationToken)
    {
        var answered = await own[_own].AnswerAsync(answer, cancellationToken);
        // Ours take rclone's errors as the answer's fault, so they'd be asked forever with nothing to reach the remote
        if (rcd.HasExited)
        {
            throw new EngineException($"{Id} couldn't be set up, as rclone stopped with exit code {rcd.ExitCode}");
        }

        _own += answered.Move;
        await AskOwnAsync(answered.Why, cancellationToken);
    }

    private async Task AskOwnAsync(string? why, CancellationToken cancellationToken)
    {
        if (_own < own.Count)
        {
            Question = own[_own].Ask() with { Error = why };
            return;
        }

        // One set up again has no root question to go back to
        if (!choices.Written)
        {
            try
            {
                await folders.WriteTestAsync(choices.Root!, cancellationToken);
            }
            catch (Exception e) when (e is RcloneException or HttpRequestException or IOException)
            {
                throw new EngineException($"{Id} couldn't be set up, as {RootQuestion.Named(choices.Root!)} can't hold files: {e.Message}", e);
            }
        }

        Question = null;
        _ended = true;
        await setups.FinishAsync(this, rcd, copy);
    }
}
