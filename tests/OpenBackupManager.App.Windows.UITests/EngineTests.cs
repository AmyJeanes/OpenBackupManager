using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.App.Windows.UITests;

[Explicit("Takes over the desktop")]
[Category("Sandbox")]
public sealed class EngineTests : AppTests
{
    [Test]
    public void TheAppStartsTheEngine_AndSaysOnceItsConnected()
    {
        WaitUntilConnected();

        Assert.That(EngineStatus()?.StartedByApp, Is.True);
    }

    [Test]
    public void Quit_StopsTheEngineTheAppStarted()
    {
        using var engine = Engine();

        Quit();

        Assert.That(engine.WaitForExit(Timeout), Is.True);
    }

    // The engine keeps running when the app doesn't quit, and says the app started it, so the next copy knows
    [Test]
    public void AfterACrash_TheNextCopyConnects_AndQuitStillStopsTheEngine()
    {
        using var engine = Engine();
        EndApp();

        LaunchAgain();
        WaitUntilConnected();
        Assert.That(EngineStatus()?.ProcessId, Is.EqualTo(engine.Id));
        Quit();

        Assert.That(engine.WaitForExit(Timeout), Is.True);
    }

    [Test]
    public void Quit_LeavesAnEngineStartedAnotherWayRunning()
    {
        using (var started = Engine())
        {
            Quit();
            // Until it has, another can't start
            Assert.That(started.WaitForExit(Timeout), Is.True);
        }

        using var engine = StartEngineByHand();
        Assert.That(Retry.WhileFalse(() => EngineStatus()?.ProcessId == engine.Id, Timeout, ignoreException: true).Result, Is.True);

        LaunchAgain();
        WaitUntilConnected();
        Assert.That(EngineStatus()?.ProcessId, Is.EqualTo(engine.Id));
        Quit();

        Assert.That(engine.WaitForExit(TimeSpan.FromSeconds(2)), Is.False);
    }

    [Test]
    public void AnEngineThatEnds_IsStartedAgain()
    {
        using var first = Engine();
        var firstStarted = first.StartTime;

        first.Kill();
        first.WaitForExit();

        // Not by process ID, which Windows can give the new engine straight after the old one ends
        Assert.That(Retry.WhileFalse(() => EngineStatus() is { } status && status.Started > firstStarted, Timeout, ignoreException: true).Result, Is.True);
        WaitUntilConnected();
    }

#if DEBUG
    // The engine writes its report as it crashes, and the app shows it once it has started another and reached it
    [Test]
    public void AnEngineCrash_IsShown_OnceTheAppReachesTheEngineAgain()
    {
        using (var engine = EngineClient.ConnectAsync(AppPaths.InHome(Home), Timeout, CancellationToken.None).GetAwaiter().GetResult()!)
        {
            engine.CrashAsync(CrashParams.OnAnotherThread, CancellationToken.None).GetAwaiter().GetResult();
        }

        var crash = WaitForWindow(AppName + "'s engine stopped unexpectedly");
        var report = crash.FindFirstDescendant(c => c.ByAutomationId("ReportText"))!.AsTextBox().Text;
        Assert.That(report, Does.StartWith("obmd closed unexpectedly at").And.Contain("System.InvalidOperationException: Test crash on another thread"));
        crash.Close();
    }
#endif

    [Test]
    public void AnEngineOfAnotherVersion_IsExplained()
    {
        using (var engine = Engine())
        {
            Quit();
            Assert.That(engine.WaitForExit(Timeout), Is.True);
        }

        using var another = new AnotherVersion(Home);
        LaunchAgain();

        Retry.WhileFalse(() => Text("StatusText") == "Can't reach the engine", Timeout, ignoreException: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Text("StatusText"), Is.EqualTo("Can't reach the engine"));
            Assert.That(Text("Detail"), Is.EqualTo("Another version of the engine is still running. Signing out and back in restarts it."));
        }
    }

    // The engine the app started, once it's connected
    private Process Engine()
    {
        WaitUntilConnected();
        return Process.GetProcessById(EngineStatus()!.ProcessId);
    }

    private string? Text(string id) => LaunchFlyout.FindFirstDescendant(c => c.ByAutomationId(id))?.Name;

    // Answers hello as an engine of another protocol does, until disposed
    private sealed class AnotherVersion : IDisposable
    {
        private readonly string _name;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _answering;

        public AnotherVersion(string home)
        {
            _name = EngineSocket.Name(AppPaths.InHome(home));
            _answering = Task.Run(() => AnswerAsync(_stopping.Token));
        }

        public void Dispose()
        {
            _stopping.Cancel();
            try
            {
                _answering.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }

            _stopping.Dispose();
        }

        private async Task AnswerAsync(CancellationToken cancellationToken)
        {
            // The next is waiting before one is answered, so the app never finds none and starts an engine
            var next = Listen();
            try
            {
                while (true)
                {
                    await next.WaitForConnectionAsync(cancellationToken);
                    await using var pipe = next;
                    next = Listen();
                    await RefuseAsync(pipe, cancellationToken);
                }
            }
            finally
            {
                await next.DisposeAsync();
            }
        }

        private static async Task RefuseAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
        {
            try
            {
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                if (await reader.ReadLineAsync(cancellationToken) is not { } line)
                {
                    return;
                }

                using var hello = JsonDocument.Parse(line);
                var id = hello.RootElement.GetProperty("id").GetRawText();
                await pipe.WriteAsync(
                    Encoding.UTF8.GetBytes($$$"""{"jsonrpc":"2.0","id":{{{id}}},"error":{"code":{{{RpcError.ProtocolMismatch}}},"message":"Another version"}}""" + "\n"),
                    cancellationToken);
            }
            catch (IOException)
            {
            }
        }

        private NamedPipeServerStream Listen() =>
            new(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }
}
