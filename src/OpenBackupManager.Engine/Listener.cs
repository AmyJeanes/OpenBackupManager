using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// Answers the tray app and obm on the engine's socket, any number at once
internal sealed partial class Listener(
    AppPaths paths,
    EngineOptions options,
    ConfigWatcher config,
    Secrets secrets,
    UpdateWatcher update,
    Remotes remotes,
    Setups setups,
    Backends backends,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<Listener> logger) : IDisposable
{
    private readonly string _name = EngineSocket.Name(paths);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _connections = [];
    private Task? _accepting;
    private DateTimeOffset _started;

    public Task StartAsync()
    {
        RunFolder.CheckRoom(paths);
        if (!OperatingSystem.IsWindows())
        {
            PrepareFolder();
        }

        var waiting = Create(first: true);
        _started = time.GetUtcNow();
        _accepting = AcceptAsync(waiting, _stopping.Token);
        LogListening(_name);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        if (_accepting is not null)
        {
            await _accepting;
        }

        Task[] open;
        lock (_connections)
        {
            open = [.. _connections];
        }

        await Task.WhenAll(open);
    }

    public void Dispose() => _stopping.Dispose();

    private StatusResult Status() =>
        new(
            AppVersion.Current,
            Environment.ProcessId,
            _started,
            paths.Config,
            paths.State,
            options.StartedBy,
            secrets.Status(),
            config.Result.Problems,
            remotes.Status(),
            update.Installed);

    // .NET leaves the socket's permissions to the umask, so a private folder keeps other users from reaching it at all
    [UnsupportedOSPlatform("windows")]
    private void PrepareFolder()
    {
        RunFolder.Create(paths);
        // As this engine holds the lock, a socket here was left by one that ended without removing it
        File.Delete(_name);
    }

    // The first is made only if nothing else has the name, so it never takes over another's
    private NamedPipeServerStream Create(bool first)
    {
        try
        {
            return new NamedPipeServerStream(
                _name,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));
        }
        catch (UnauthorizedAccessException e) when (first)
        {
            throw new EngineException($"Something else is already listening at {_name}, where the engine listens", e);
        }
    }

    private async Task AcceptAsync(NamedPipeServerStream waiting, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    await waiting.WaitForConnectionAsync(cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    // Another user, on macOS and Linux. The pipe goes on waiting for someone else
                    LogRefused();
                    continue;
                }
                catch (IOException e) when (OperatingSystem.IsWindows())
                {
                    // A client that left as it connected breaks the pipe on Windows. Replaced before it's let go, so
                    // there's always one waiting and nothing else can take the name
                    var broken = waiting;
                    waiting = Create(first: false);
                    await broken.DisposeAsync();
                    LogLeftAsItConnected(e.Message);
                    continue;
                }

                var connected = waiting;
                // Before serving this one, so there's always one waiting
                waiting = Create(first: false);
                lock (_connections)
                {
                    _connections.RemoveAll(c => c.IsCompleted);
                    _connections.Add(ServeAsync(connected, cancellationToken));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            LogStoppedListening(e);
        }
        finally
        {
            await waiting.DisposeAsync();
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            // Cancelled once the client goes, as well as when the engine stops, so what it asked stops with it, such
            // as a sign-in that waits for a browser, rather than running on
            using var open = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var reading = ReadAsync(pipe, lines.Writer, open);
            try
            {
                await using var connection = new Connection(Status, config, secrets, remotes, setups, backends, logger);
                await foreach (var line in lines.Reader.ReadAllAsync(open.Token))
                {
                    if (await connection.AnswerAsync(line, open.Token) is { } response)
                    {
                        await pipe.WriteAsync(EngineProtocol.Line(response, ProtocolJson.Default.RpcResponse), open.Token);
                        await pipe.FlushAsync(open.Token);
                    }

                    if (connection.Stopping)
                    {
                        LogAskedToStop();
                        lifetime.StopApplication();
                    }

                    if (connection.Closing)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopping, or it went away during a call, as a connection answers anything else cancelled as failed
            }
            catch (IOException)
            {
                // It went away
            }
            finally
            {
                await open.CancelAsync();
                await reading;
            }
        }
    }

    // All along, not only between calls, so a client that goes is seen during one. Calls are still answered in turn
    private static async Task ReadAsync(NamedPipeServerStream pipe, ChannelWriter<string> lines, CancellationTokenSource open)
    {
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (await reader.ReadLineAsync(open.Token) is { } line)
            {
                await lines.WriteAsync(line, open.Token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
        }
        finally
        {
            lines.Complete();
            await open.CancelAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening at {Socket}")]
    private partial void LogListening(string socket);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping, as a client asked")]
    private partial void LogAskedToStop();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a connection from another user")]
    private partial void LogRefused();

    [LoggerMessage(Level = LogLevel.Information, Message = "A client left as it connected: {Reason}")]
    private partial void LogLeftAsItConnected(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopped listening, so nothing more can connect")]
    private partial void LogStoppedListening(Exception exception);
}
