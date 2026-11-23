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

    private string Secrets => Path.Combine(_home.State, "secrets.json");

    [SetUp]
    public void SetUp()
    {
        _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-remotes-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(_home.Config);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_home.State, recursive: true);

    [Test]
    public async Task EachRemote_HasItsOwnRclone_UntilTheEngineStops()
    {
        await RemotesAsync("onedrive", "nas");
        using var engine = EngineHost.Build(_home);
        await engine.StartAsync();
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
        using var engine = EngineHost.Build(_home);
        await engine.StartAsync();
        var socket = RemotesOf(engine).Options("onedrive").Socket;
        using var first = Process.GetProcessById(await PidAsync(socket));

        first.Kill();

        Assert.That(await PidAsync(socket, notPid: first.Id), Is.Not.EqualTo(first.Id));
        await engine.StopAsync();
    }

    [Test]
    public async Task ItsRclone_WhenItCantStart_IsTriedAgain()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        // Unreadable until it's fixed
        await File.WriteAllTextAsync(Secrets, "not JSON");
        using var engine = EngineHost.Build(_home);
        var (remotes, clock, log) = Unstarted(engine);
        await remotes.StartAsync(CancellationToken.None);

        var wait = await NextAsync(clock.Waits);
        Assert.That(await log.LineAsync("Couldn't start"), Does.Contain("onedrive"));
        File.Delete(Secrets);
        await SavePasswordAsync("password");
        clock.Advance(wait);

        Assert.That(await PidAsync(remotes.Options("onedrive").Socket), Is.Positive);
        await remotes.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ItsRclone_WhenItKeepsStopping_WaitsLongerEachTime_UntilItStaysUp()
    {
        await RemotesAsync("onedrive");
        using var engine = EngineHost.Build(_home);
        var (remotes, clock, log) = Unstarted(engine);
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
        await remotes.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task WithNoRcloneConfig_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        File.Delete(Path.Combine(_home.State, "rclone", "onedrive.conf"));
        using var engine = EngineHost.Build(_home);
        await engine.StartAsync();

        Assert.That(async () => await RemotesOf(engine).Running.WaitAsync(Timeout), Throws.Nothing);
        Assert.That(RemotesOf(engine).Options("onedrive").Socket, Does.Not.Exist);
        await engine.StopAsync();
    }

    // And makes no new password
    [Test]
    public async Task WhenItsPasswordIsMissing_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        using var engine = EngineHost.Build(_home);
        await engine.StartAsync();

        Assert.That(async () => await RemotesOf(engine).Running.WaitAsync(Timeout), Throws.Nothing);
        using var store = new FileStore(Secrets);
        Assert.That(await store.ReadAsync("rclone-config"), Is.Null);
        await engine.StopAsync();
    }

    [Test]
    public async Task WhenItsPasswordIsWrong_ItNeedsSettingUp()
    {
        await RemotesAsync("onedrive");
        await EncryptedConfigAsync("onedrive", "password");
        await SavePasswordAsync("another");
        using var engine = EngineHost.Build(_home);
        var (remotes, _, log) = Unstarted(engine);
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
        using var engine = EngineHost.Build(_home);
        var (remotes, _, log) = Unstarted(engine);
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
        using var engine = EngineHost.Build(_home);
        await engine.StartAsync();
        var socket = RemotesOf(engine).Options("onedrive").Socket;
        await PidAsync(socket);

        using var client = new RcClient(socket);
        Assert.That((await client.ListRemotesAsync(CancellationToken.None)).Remotes, Is.EqualTo(["test"]));
        await engine.StopAsync();
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

    // Its own clock, as the engine's timers would move with it
    private (Remotes Remotes, Clock Clock, LogLines Log) Unstarted(IHost engine)
    {
        var clock = new Clock();
        var log = new LogLines();
        return (new Remotes(engine.Services.GetRequiredService<ConfigWatcher>(), engine.Services.GetRequiredService<RcloneConfig>(), _home, clock, log), clock, log);
    }

    private static async Task<T> NextAsync<T>(ChannelReader<T> reader)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        return await reader.ReadAsync(timeout.Token);
    }

    private static Remotes RemotesOf(IHost engine) => engine.Services.GetServices<IHostedService>().OfType<Remotes>().Single();

    // With plain rclone configs, and the file store, which every OS has
    private async Task RemotesAsync(params string[] ids)
    {
        await File.WriteAllTextAsync(_home.ConfigFile, "secret-store = \"file\"\n" + string.Concat(ids.Select(id => $"\n[[remotes]]\nid = \"{id}\"\n")));
        Directory.CreateDirectory(Path.Combine(_home.State, "rclone"));
        foreach (var id in ids)
        {
            await File.WriteAllTextAsync(Path.Combine(_home.State, "rclone", id + ".conf"), "[test]\ntype = local\n");
        }
    }

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
                null,
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

    // The engine starts each without waiting for it, and the one it's replacing may still answer for a moment
    private static async Task<int> PidAsync(string socket, int? notPid = null)
    {
        using var client = new RcClient(socket);
        var until = DateTime.UtcNow + Timeout;
        while (true)
        {
            try
            {
                var pid = (await client.PidAsync(CancellationToken.None)).Pid;
                if (pid != notPid)
                {
                    return pid;
                }
            }
            catch (HttpRequestException) when (DateTime.UtcNow < until)
            {
            }

            Assert.That(DateTime.UtcNow, Is.LessThan(until), $"Nothing new answered on {socket}");
            await Task.Delay(100);
        }
    }
}

// Says how long each wait is for, so a test moves the clock on only once it's waiting
internal sealed class Clock : FakeTimeProvider
{
    private readonly Channel<TimeSpan> _waits = Channel.CreateUnbounded<TimeSpan>();

    public ChannelReader<TimeSpan> Waits => _waits.Reader;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
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
