using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine.Tests;

public class ListenerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private AppPaths _home = null!;
    private IHost? _engine;

    [SetUp]
    public void SetUp() => _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-engine-" + Guid.NewGuid().ToString("N")[..8]));

    [TearDown]
    public async Task TearDown()
    {
        if (_engine is not null)
        {
            await _engine.StopAsync();
            _engine.Dispose();
            _engine = null;
        }

        if (Directory.Exists(_home.State))
        {
            Directory.Delete(_home.State, recursive: true);
        }
    }

    [Test]
    public async Task Status_DescribesTheEngine()
    {
        var before = DateTimeOffset.UtcNow;
        await StartAsync();

        using var client = await ConnectAsync();
        var status = await client.StatusAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Version, Is.EqualTo(AppVersion.Current));
            Assert.That(status.ProcessId, Is.EqualTo(Environment.ProcessId));
            Assert.That(status.Started, Is.InRange(before, DateTimeOffset.UtcNow));
            Assert.That(status.Config, Is.EqualTo(_home.Config));
            Assert.That(status.State, Is.EqualTo(_home.State));
            Assert.That(status.StartedByApp, Is.False);
        }
    }

    [Test]
    public async Task Status_SaysTheAppStartedIt()
    {
        await StartAsync(startedByApp: true);

        using var client = await ConnectAsync();

        Assert.That((await client.StatusAsync(CancellationToken.None)).StartedByApp, Is.True);
    }

    [Test]
    public async Task Stop_StopsTheEngine()
    {
        // Run, as obmd does, since only that stops when the engine asks to
        var running = EngineHost.Build(_home).RunAsync();
        using var client = await ConnectAsync();

        await client.StopAsync(CancellationToken.None);

        Assert.That(async () => await running.WaitAsync(Timeout), Throws.Nothing);
    }

    [Test]
    public async Task Closed_WhenTheEngineStops()
    {
        await StartAsync();
        using var client = await ConnectAsync();

        await _engine!.StopAsync();

        Assert.That(async () => await client.Closed.WaitAsync(Timeout), Throws.Nothing);
    }

    [Test]
    public async Task ACall_OnceTheEngineHasStopped_SaysSo()
    {
        await StartAsync();
        using var client = await ConnectAsync();
        await _engine!.StopAsync();
        await client.Closed.WaitAsync(Timeout);

        Assert.That(() => client.StatusAsync(CancellationToken.None), Throws.TypeOf<EngineException>());
    }

    [Test]
    public async Task OverlappingCalls_EachGetTheirOwnAnswer()
    {
        await StartAsync();
        using var client = await ConnectAsync();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.StatusAsync(CancellationToken.None)));

        Assert.That(statuses.Select(s => s.ProcessId), Is.All.EqualTo(Environment.ProcessId));
    }

    [Test]
    public async Task SeveralClients_AtOnce()
    {
        await StartAsync();

        var clients = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ConnectAsync()));
        try
        {
            var statuses = await Task.WhenAll(clients.Select(c => c.StatusAsync(CancellationToken.None)));

            Assert.That(statuses.Select(s => s.ProcessId), Is.All.EqualTo(Environment.ProcessId));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Test]
    public async Task AClientThatLeaves_DoesntStopOthers()
    {
        await StartAsync();

        (await ConnectAsync()).Dispose();
        using var client = await ConnectAsync();

        Assert.That(await client.StatusAsync(CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task NoEngine_IsNotRunning()
    {
        using var client = await EngineClient.ConnectAsync(_home, TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.That(client, Is.Null);
    }

    [Test]
    public async Task AnotherProtocol_IsRefused()
    {
        await StartAsync();
        using var raw = await RawAsync();

        var response = await raw.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"hello","params":{"protocol":999,"version":"9.9.9"}}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(RpcError.ProtocolMismatch));
            Assert.That(response.GetProperty("error").GetProperty("message").GetString(), Does.Contain("9.9.9").And.Contain(AppVersion.Current));
            Assert.That(await raw.ReadLineAsync(), Is.Null, "The engine closes the connection");
        }
    }

    [Test]
    public async Task AnythingBeforeHello_IsRefused()
    {
        await StartAsync();
        using var raw = await RawAsync();

        var response = await raw.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"status"}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(RpcError.HelloFirst));
            Assert.That(await raw.ReadLineAsync(), Is.Null, "The engine closes the connection");
        }
    }

    [TestCase("not json", RpcError.ParseError)]
    [TestCase("""{"id":2,"method":"status"}""", RpcError.InvalidRequest)]
    [TestCase("""{"jsonrpc":"2.0","id":2,"method":"nothing"}""", RpcError.MethodNotFound)]
    public async Task ABadRequest_IsAnsweredWithAnError_AndTheConnectionGoesOn(string request, int code)
    {
        await StartAsync();
        using var raw = await RawAsync();
        await raw.HelloAsync();

        var response = await raw.SendAsync(request);
        var status = await raw.SendAsync("""{"jsonrpc":"2.0","id":3,"method":"status"}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(code));
            Assert.That(status.GetProperty("result").GetProperty("processId").GetInt32(), Is.EqualTo(Environment.ProcessId));
        }
    }

    [Test]
    public async Task AHelloMissingItsVersion_IsInvalid()
    {
        await StartAsync();
        using var raw = await RawAsync();

        var response = await raw.SendAsync("""{"jsonrpc":"2.0","id":1,"method":"hello","params":{"protocol":1}}""");

        Assert.That(response.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(RpcError.InvalidParams));
    }

    [Test]
    public async Task ARequestsId_IsSentBack()
    {
        await StartAsync();
        using var raw = await RawAsync();

        var response = await raw.SendAsync(
            $$$"""{"jsonrpc":"2.0","id":"abc","method":"hello","params":{"protocol":{{{EngineProtocol.Version}}},"version":"x"}}""");

        Assert.That(response.GetProperty("id").GetString(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task Stopping_ClosesEachConnection()
    {
        await StartAsync();
        using var raw = await RawAsync();
        await raw.HelloAsync();

        await _engine!.StopAsync();

        Assert.That(await raw.ReadLineAsync(), Is.Null);
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public async Task TheSocketsFolder_IsPrivate()
    {
        Directory.CreateDirectory(EngineSocket.Folder(_home));
        File.SetUnixFileMode(EngineSocket.Folder(_home), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        await StartAsync();

        Assert.That(File.GetUnixFileMode(EngineSocket.Folder(_home)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task ASocketLeftBehind_IsReplaced()
    {
        Directory.CreateDirectory(EngineSocket.Folder(_home));
        await File.WriteAllTextAsync(EngineSocket.Name(_home), "");

        await StartAsync();

        using var client = await ConnectAsync();
        Assert.That(await client.StatusAsync(CancellationToken.None), Is.Not.Null);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task Stopping_RemovesTheSocket()
    {
        await StartAsync();

        await _engine!.StopAsync();

        Assert.That(File.Exists(EngineSocket.Name(_home)), Is.False);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task AHomeTooLongForASocket_SaysWhy()
    {
        _home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm-engine-" + new string('x', 120)));

        var e = await Assert.ThrowsAsync<EngineException>(() => StartAsync());

        Assert.That(e.Message, Does.Contain("--home"));
    }

    [Test]
    [Platform("Win")]
    public async Task APipeAlreadyThere_StopsItStarting()
    {
        using var squatter = new NamedPipeServerStream(EngineSocket.Name(_home), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances);

        var e = await Assert.ThrowsAsync<EngineException>(() => StartAsync());

        Assert.That(e.Message, Does.Contain("already listening"));
    }

    private async Task StartAsync(bool startedByApp = false)
    {
        _engine = EngineHost.Build(_home, startedByApp);
        await _engine.StartAsync();
    }

    private async Task<EngineClient> ConnectAsync() =>
        await EngineClient.ConnectAsync(_home, Timeout, CancellationToken.None) ?? throw new AssertionException("The engine isn't running");

    private async Task<Raw> RawAsync()
    {
        var pipe = new NamedPipeClientStream(".", EngineSocket.Name(_home), PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(Timeout, CancellationToken.None);
        return new Raw(pipe);
    }

    // Writes lines as any client could, right or wrong
    private sealed class Raw(NamedPipeClientStream pipe) : IDisposable
    {
        private readonly StreamReader _reader = new(pipe, new UTF8Encoding(false));
        private readonly StreamWriter _writer = new(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        public async Task HelloAsync() =>
            await SendAsync($$$"""{"jsonrpc":"2.0","id":0,"method":"hello","params":{"protocol":{{{EngineProtocol.Version}}},"version":"test"}}""");

        public async Task<JsonElement> SendAsync(string line)
        {
            await _writer.WriteLineAsync(line);
            var response = await ReadLineAsync() ?? throw new AssertionException("The engine closed the connection");
            return JsonDocument.Parse(response).RootElement;
        }

        public async Task<string?> ReadLineAsync()
        {
            using var timeout = new CancellationTokenSource(Timeout);
            return await _reader.ReadLineAsync(timeout.Token);
        }

        // The reader and writer hold nothing of their own, and the writer would throw writing to a pipe the engine closed
        public void Dispose() => pipe.Dispose();
    }
}
