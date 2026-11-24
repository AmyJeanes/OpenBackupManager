using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine.Tests;

public class RemotesTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private AppPaths _home = null!;
    private IHost? _engine;
    private IHost? _services;
    private Remotes? _remotes;

    private string Secrets => Path.Combine(_home.State, "secrets.json");

    [SetUp]
    public void SetUp()
    {
        _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-remotes-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(_home.Config);
    }

    // Stopped here, so a failed assertion doesn't leave rcds running
    [TearDown]
    public async Task TearDown()
    {
        if (_remotes is not null)
        {
            await _remotes.StopAsync(CancellationToken.None);
            _remotes.Dispose();
            _remotes = null;
        }

        if (_engine is not null)
        {
            await _engine.StopAsync();
            _engine.Dispose();
            _engine = null;
        }

        _services?.Dispose();
        _services = null;
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
    public async Task EachRemote_HasItsOwnRclone_UntilTheEngineStops()
    {
        await RemotesAsync("onedrive", "nas");
        var engine = await StartedAsync();
        string[] sockets = [RemotesOf(engine).Options("onedrive").Socket, RemotesOf(engine).Options("nas").Socket];

        var pids = new List<int>();
        foreach (var socket in sockets)
        {
            pids.Add(await PidAsync(socket));
        }

        await engine.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pids, Is.Unique);
            // Each quit rather than was killed, as that leaves its socket
            Assert.That(sockets.Where(File.Exists), Is.Empty);
        }
    }

    [Test]
    public async Task ItsRclone_WhenItStops_StartsAgain()
    {
        await RemotesAsync("onedrive");
        var (remotes, clock, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        // Not while it's starting, which would be a failed start
        await log.LineAsync("Started rclone");
        using (var first = Process.GetProcessById(await PidAsync(remotes.Options("onedrive").Socket)))
        {
            first.Kill();
        }

        clock.Advance(await NextAsync(clock.Waits));

        // Rather than a new process ID, as Windows can give the new one the same
        Assert.That(await log.LineAsync("Started rclone"), Does.Contain("onedrive"));
    }

    [Test]
    public async Task ItsRclone_WhenItCantStart_IsTriedAgain()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        // Unreadable until it's fixed
        await File.WriteAllTextAsync(Secrets, "not JSON");
        var (remotes, clock, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);

        var wait = await NextAsync(clock.Waits);
        Assert.That(await log.LineAsync("Couldn't start"), Does.Contain("onedrive"));
        File.Delete(Secrets);
        await SavePasswordAsync("password");
        clock.Advance(wait);

        Assert.That(await PidAsync(remotes.Options("onedrive").Socket), Is.Positive);
    }

    // Rather than it ending unseen, until the engine stops and throws it
    [Test]
    public async Task ItsRclone_WhenABugStopsIt_SaysSo()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await File.WriteAllTextAsync(Secrets, "not JSON");
        var (remotes, clock, log) = Unstarted();
        clock.Fails = new NotSupportedException("A bug");
        await remotes.StartAsync(CancellationToken.None);

        Assert.That(await log.LineAsync("Stopped running"), Does.Contain("onedrive"));
        Assert.That(async () => await remotes.Running.WaitAsync(Timeout), Throws.Nothing);
    }

    [Test]
    public async Task Status_WhenItsRunning_SaysSo()
    {
        await RemotesAsync("onedrive");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        await log.LineAsync("Started rclone");

        Assert.That(remotes.Status(), Is.EqualTo((RemoteStatus[])[new("onedrive", RemoteState.Running)]));
    }

    [Test]
    public async Task Status_WhenItCantStart_SaysWhyAndWhenItTriesAgain()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await File.WriteAllTextAsync(Secrets, "not JSON");
        var (remotes, clock, _) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);

        var wait = await NextAsync(clock.Waits);

        var status = remotes.Status().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.State, Is.EqualTo(RemoteState.Retrying));
            Assert.That(status.Reason, Is.EqualTo(RemoteReason.CouldntStart));
            Assert.That(status.NextTry, Is.EqualTo(clock.GetUtcNow() + wait));
        }
    }

    [Test]
    public async Task Status_WhenItNeedsSettingUp_SaysWhy()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        await log.LineAsync("needs setting up");

        Assert.That(remotes.Status(), Is.EqualTo((RemoteStatus[])[new("onedrive", RemoteState.NeedsSetup, RemoteReason.NoPassword)]));
    }

    [Test]
    public async Task Status_WhenABugStopsIt_SaysWhatItWas()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await File.WriteAllTextAsync(Secrets, "not JSON");
        var (remotes, clock, log) = Unstarted();
        clock.Fails = new NotSupportedException("A bug");
        await remotes.StartAsync(CancellationToken.None);
        await log.LineAsync("Stopped running");

        Assert.That(remotes.Status(), Is.EqualTo((RemoteStatus[])[new("onedrive", RemoteState.Stopped, RemoteReason.Bug, "A bug")]));
    }

    [Test]
    public async Task Retry_WhenItNeedsSettingUp_StartsItAgain()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        await log.LineAsync("needs setting up");
        await SavePasswordAsync("password");

        remotes.Retry("onedrive");

        await log.LineAsync("Started rclone");
        Assert.That(remotes.Status().Single().State, Is.EqualTo(RemoteState.Running));
    }

    [Test]
    public async Task Retry_WhenABugStoppedIt_StartsItAgain()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await File.WriteAllTextAsync(Secrets, "not JSON");
        var (remotes, clock, log) = Unstarted();
        clock.Fails = new NotSupportedException("A bug");
        await remotes.StartAsync(CancellationToken.None);
        await log.LineAsync("Stopped running");
        clock.Fails = null;
        File.Delete(Secrets);
        await SavePasswordAsync("password");

        remotes.Retry("onedrive");

        Assert.That(await log.LineAsync("Started rclone"), Does.Contain("onedrive"));
    }

    // Rather than when it would have
    [Test]
    public async Task Retry_WhenItsWaitingToTryAgain_TriesNow()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await File.WriteAllTextAsync(Secrets, "not JSON");
        var (remotes, clock, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        await NextAsync(clock.Waits);
        File.Delete(Secrets);
        await SavePasswordAsync("password");

        remotes.Retry("onedrive");

        Assert.That(await log.LineAsync("Started rclone"), Does.Contain("onedrive"));
    }

    [Test]
    public async Task Retry_ARemoteThatIsntThere_Fails()
    {
        await RemotesAsync("onedrive");
        var (remotes, _, _) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);

        Assert.That(() => remotes.Retry("nas"), Throws.TypeOf<EngineException>().With.Message.EqualTo("There's no remote called nas"));
    }

    [Test]
    public async Task AddedToConfig_ItStarts()
    {
        await RemotesAsync("onedrive");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        await log.LineAsync("Started rclone");
        await File.WriteAllTextAsync(Path.Combine(_home.State, "rclone", "nas.conf"), "[test]\ntype = local\n");
        await ConfigAsync("onedrive", "nas");

        Reload();

        Assert.That(await log.LineAsync("Started rclone"), Does.Contain("nas"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await PidAsync(remotes.Options("nas").Socket), Is.Positive);
            Assert.That(remotes.Status().Select(status => status.Id), Is.EqualTo(["onedrive", "nas"]));
        }
    }

    [Test]
    public async Task TakenOutOfConfig_ItStops()
    {
        await RemotesAsync("onedrive", "nas");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        using var nas = await RcloneAsync(remotes, log, "nas");
        await ConfigAsync("onedrive");

        Reload();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remotes.Status().Select(status => status.Id), Is.EqualTo(["onedrive"]));
            Assert.That(nas.WaitForExit(Timeout), Is.True);
        }
    }

    // Not while its last one is still quitting, which starting stops first, as it still answers on its socket
    [Test]
    public async Task TakenOutAndPutBack_ItStartsAgain_AfterItsLastRcloneHasGone()
    {
        await RemotesAsync("onedrive");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        using var first = await RcloneAsync(remotes, log, "onedrive");
        var gone = first.WaitForExitAsync().ContinueWith(_ => DateTime.Now, TaskScheduler.Default);
        await ConfigAsync();
        Reload();
        await ConfigAsync("onedrive");

        Reload();

        using var second = await RcloneAsync(remotes, log, "onedrive");
        // Allowing for when this sees it go, as rcd takes over a second to quit
        Assert.That(second.StartTime, Is.GreaterThan(await gone - TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    public async Task ConfigWithAnError_ChangesNothing()
    {
        await RemotesAsync("onedrive");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        using var onedrive = await RcloneAsync(remotes, log, "onedrive");
        await File.WriteAllTextAsync(_home.ConfigFile, "version = \"one\"\n");

        Reload();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remotes.Status(), Is.EqualTo((RemoteStatus[])[new("onedrive", RemoteState.Running)]));
            Assert.That(onedrive.HasExited, Is.False);
        }
    }

    [Test]
    public async Task ItsRclone_WhenItKeepsStopping_WaitsLongerEachTime_UntilItStaysUp()
    {
        await RemotesAsync("onedrive");
        var (remotes, clock, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        var socket = remotes.Options("onedrive").Socket;

        var waits = new List<double>();
        for (var i = 0; i < 9; i++)
        {
            // Not while it's starting, which would be a failed start
            await log.LineAsync("Started rclone");
            // Up a minute before the last
            if (i == 8)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
            }

            using (var rclone = Process.GetProcessById(await PidAsync(socket)))
            {
                rclone.Kill();
            }

            var wait = await NextAsync(clock.Waits);
            Assert.That(await log.LineAsync("restarting in"), Does.EndWith($"restarting in {wait.TotalSeconds} s"));
            waits.Add(wait.TotalSeconds);
            clock.Advance(wait);
        }

        Assert.That(waits, Is.EqualTo((double[])[1, 2, 4, 8, 16, 32, 60, 60, 1]));
    }

    [Test]
    public async Task WithNoRcloneConfig_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        File.Delete(Path.Combine(_home.State, "rclone", "onedrive.conf"));
        var engine = await StartedAsync();

        Assert.That(async () => await RemotesOf(engine).Running.WaitAsync(Timeout), Throws.Nothing);
        Assert.That(RemotesOf(engine).Options("onedrive").Socket, Does.Not.Exist);
    }

    // And makes no new password
    [Test]
    public async Task WhenItsPasswordIsMissing_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        var engine = await StartedAsync();

        Assert.That(async () => await RemotesOf(engine).Running.WaitAsync(Timeout), Throws.Nothing);
        using var store = new FileStore(Secrets);
        Assert.That(await store.ReadAsync("rclone-config"), Is.Null);
    }

    [Test]
    public async Task WhenItsPasswordIsWrong_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await SavePasswordAsync("another");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);

        Assert.That(async () => await remotes.Running.WaitAsync(Timeout), Throws.Nothing);
        var line = await log.LineAsync("needs setting up");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(line, Does.Contain("with the password in the secret store"));
            // Stopped, not left running
            Assert.That(remotes.Options("onedrive").Socket, Does.Not.Exist);
        }
    }

    // Without quoting it, as a damaged line may be part of a token
    [Test]
    public async Task WhenItsConfigIsDamaged_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        await File.WriteAllTextAsync(Path.Combine(_home.State, "rclone", "onedrive.conf"), "[test]\ntype = local\nhalf a token\n");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);

        Assert.That(async () => await remotes.Running.WaitAsync(Timeout), Throws.Nothing);
        var line = await log.LineAsync("needs setting up");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(line, Does.Contain("rclone can't read its config").And.Contain(remotes.Options("onedrive").LogFile));
            Assert.That(line, Does.Not.Contain("half a token"));
        }
    }

    [Test]
    public async Task WithItsPassword_ItReadsItsEncryptedConfig()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await SavePasswordAsync("password");
        var (remotes, _, log) = Unstarted();
        await remotes.StartAsync(CancellationToken.None);
        // Not before it's been given the password, which is once it answers
        await log.LineAsync("Started rclone");

        using var client = new RcClient(remotes.Options("onedrive").Socket);
        Assert.That((await client.ListRemotesAsync(CancellationToken.None)).Remotes, Is.EqualTo(["test"]));
    }

    // Where whatever rclone starts would get it too
    [Test]
    [Platform("Linux")]
    public async Task WithItsPassword_KeepsItOutOfRclonesEnvironment()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await SavePasswordAsync("password");
        var engine = await StartedAsync();
        var pid = await PidAsync(RemotesOf(engine).Options("onedrive").Socket);

        // Only the names, so a failure doesn't print every value
        var names = (await File.ReadAllTextAsync($"/proc/{pid}/environ")).Split('\0').Select(variable => variable.Split('=')[0]);
        Assert.That(names, Does.Not.Contain("RCLONE_CONFIG_PASS"));
    }

    // Where a parent rclone leaves its config's key, which rclone would use instead of the password
    [Test]
    public async Task WithItsPassword_IgnoresTheUsersConfigKeyFile()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await SavePasswordAsync("password");
        var keyFile = Path.Combine(_home.State, "key");
        await File.WriteAllTextAsync(keyFile, "not the key");
        Environment.SetEnvironmentVariable("_RCLONE_CONFIG_KEY_FILE", keyFile);
        try
        {
            var (remotes, _, log) = Unstarted();
            await remotes.StartAsync(CancellationToken.None);

            Assert.That(await log.LineAsync("onedrive"), Does.StartWith("Started rclone"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("_RCLONE_CONFIG_KEY_FILE", null);
        }
    }

    [Test]
    public async Task PasswordForSetup_WithNone_MakesAndSavesOne()
    {
        await RemotesAsync();
        using var engine = EngineHost.Build(_home);
        var config = engine.Services.GetRequiredService<RcloneConfig>();

        var made = await config.PasswordForSetupAsync(CancellationToken.None);
        var again = await config.PasswordForSetupAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(made.Reveal(), Has.Length.EqualTo(40));
            Assert.That(again.Reveal(), Is.EqualTo(made.Reveal()));
            Assert.That((await config.ReadPasswordAsync(CancellationToken.None))?.Reveal(), Is.EqualTo(made.Reveal()));
        }
    }

    [Test]
    public async Task PasswordForSetup_ManyAtOnce_MakesOne()
    {
        await RemotesAsync();
        using var engine = EngineHost.Build(_home);
        var config = engine.Services.GetRequiredService<RcloneConfig>();

        var passwords = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => config.PasswordForSetupAsync(CancellationToken.None))));

        Assert.That(passwords.Select(p => p.Reveal()).Distinct(), Is.EqualTo([(await config.ReadPasswordAsync(CancellationToken.None))!.Reveal()]));
    }

    // Only the old password can read them
    [Test]
    public async Task PasswordForSetup_WithNone_ButEncryptedConfigs_MakesNone()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        using var engine = EngineHost.Build(_home);
        var config = engine.Services.GetRequiredService<RcloneConfig>();

        Assert.That(() => config.PasswordForSetupAsync(CancellationToken.None),
            Throws.TypeOf<EngineException>().With.Message.Contains("need setting up again"));
        Assert.That(await config.ReadPasswordAsync(CancellationToken.None), Is.Null);
    }

    [Test]
    public void Options_NameItsFilesFromItsId()
    {
        using var engine = EngineHost.Build(_home);
        var remotes = RemotesOf(engine);

        var options = remotes.Options("onedrive-2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Rclone, Is.EqualTo(BundledRclone.FilePath));
            Assert.That(options.Config, Is.EqualTo(Path.Combine(_home.State, "rclone", "onedrive-2.conf")));
            Assert.That(options.CacheFolder, Is.EqualTo(Path.Combine(_home.Cache, "rclone", "onedrive-2")));
            Assert.That(options.LogFile, Is.EqualTo(Path.Combine(_home.Logs, "rclone-onedrive-2.log")));
            Assert.That(Path.GetFileName(options.Socket), Does.Match("^rc-[0-9a-f]{8}\\.sock$"));
            Assert.That(options.Socket, Is.Not.EqualTo(remotes.Options("onedrive").Socket));
            Assert.That(options.UserAgent, Is.EqualTo($"ISV|AmyJeanes|OpenBackupManager/{AppVersion.Current}"));
        }
    }

    private async Task<IHost> StartedAsync()
    {
        _engine = EngineHost.Build(_home);
        await _engine.StartAsync();
        return _engine;
    }

    // With the engine's config and secrets, but its own clock, as the engine's timers would move with it
    private (Remotes Remotes, Clock Clock, LogLines Log) Unstarted()
    {
        _services = EngineHost.Build(_home);
        var clock = new Clock();
        var log = new LogLines();
        _remotes = new Remotes(_services.Services.GetRequiredService<ConfigWatcher>(), _services.Services.GetRequiredService<RcloneConfig>(), _home, clock, log);
        return (_remotes, clock, log);
    }

    private static async Task<T> NextAsync<T>(ChannelReader<T> reader)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        return await reader.ReadAsync(timeout.Token);
    }

    private static Remotes RemotesOf(IHost engine) => engine.Services.GetRequiredService<Remotes>();

    // With plain rclone configs, and the file store, which every OS has
    private async Task RemotesAsync(params string[] ids)
    {
        await ConfigAsync(ids);
        Directory.CreateDirectory(Path.Combine(_home.State, "rclone"));
        foreach (var id in ids)
        {
            await File.WriteAllTextAsync(Path.Combine(_home.State, "rclone", id + ".conf"), "[test]\ntype = local\n");
        }
    }

    private Task ConfigAsync(params string[] ids) =>
        File.WriteAllTextAsync(_home.ConfigFile, "secret-store = \"file\"\n" + string.Concat(ids.Select(id => $"\n[[remotes]]\nid = \"{id}\"\n")));

    // Rather than waiting for the engine to see the file change
    private void Reload() => _services!.Services.GetRequiredService<ConfigWatcher>().Reload();

    private async Task SavePasswordAsync(string password)
    {
        using var store = new FileStore(Secrets);
        await store.SaveAsync("rclone-config", new Secret(password));
    }

    // As setup will make it
    private async Task EncryptedConfigAsync(string id, string password)
    {
        var file = Path.Combine(_home.State, "rclone", id + ".conf");
        File.Delete(file);
        var folder = Directory.CreateTempSubdirectory("obm-rc-").FullName;
        try
        {
            await using var rcd = await Rcd.StartAsync(
                new RcdOptions(BundledRclone.FilePath, Path.Combine(folder, "rc.sock"), file, Path.Combine(folder, "cache"), Path.Combine(folder, "rclone.log"), "Tests"),
                CancellationToken.None);
            await rcd.Client.UnlockConfigAsync(password, CancellationToken.None);
            await rcd.Client.CallAsync(
                "config/create", new CreateParams("test", "local", [], new CreateOptions(true)), TestJson.Default.CreateParams, TestJson.Default.JsonElement, CancellationToken.None);
        }
        finally
        {
            // Windows may still hold rclone's log
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        Assert.That(RcloneConfigFile.IsEncrypted(file), Is.True);
    }

    // Once it's started
    private static async Task<Process> RcloneAsync(Remotes remotes, LogLines log, string id)
    {
        while (!(await log.LineAsync("Started rclone")).Contains(id, StringComparison.Ordinal))
        {
        }

        return Process.GetProcessById(await PidAsync(remotes.Options(id).Socket));
    }

    // The engine starts each without waiting for it
    private static async Task<int> PidAsync(string socket)
    {
        using var client = new RcClient(socket);
        var until = DateTime.UtcNow + Timeout;
        while (true)
        {
            try
            {
                return (await client.PidAsync(CancellationToken.None)).Pid;
            }
            catch (HttpRequestException) when (DateTime.UtcNow < until)
            {
            }

            Assert.That(DateTime.UtcNow, Is.LessThan(until), $"Nothing answered on {socket}");
            await Task.Delay(100);
        }
    }
}

// Says how long each wait is for, so a test moves the clock on only once it's waiting
internal sealed class Clock : FakeTimeProvider
{
    private readonly Channel<TimeSpan> _waits = Channel.CreateUnbounded<TimeSpan>();

    public ChannelReader<TimeSpan> Waits => _waits.Reader;

    // Thrown by any wait while it's set
    public Exception? Fails { get; set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (Fails is not null)
        {
            throw Fails;
        }

        var timer = base.CreateTimer(callback, state, dueTime, period);
        _waits.Writer.TryWrite(dueTime);
        return timer;
    }
}

internal sealed class LogLines : ILogger<Remotes>
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _lines.Writer.TryWrite(formatter(state, exception));

    // The next with this in it, skipping any before it
    public async Task<string> LineAsync(string containing)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var line = await _lines.Reader.ReadAsync(timeout.Token);
            if (line.Contains(containing, StringComparison.Ordinal))
            {
                return line;
            }
        }
    }
}

internal sealed record CreateParams(string Name, string Type, Dictionary<string, string> Parameters, CreateOptions Opt);

internal sealed record CreateOptions(bool NonInteractive);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CreateParams))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class TestJson : JsonSerializerContext;
