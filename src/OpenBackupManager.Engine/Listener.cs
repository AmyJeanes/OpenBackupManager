using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// Answers the tray app and obm on the engine's socket, any number at once
internal sealed partial class Listener(
    AppPaths paths,
    EngineOptions options,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<Listener> logger) : IDisposable
{
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _name = EngineSocket.Name(paths);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _connections = [];
    private Task? _accepting;
    private DateTimeOffset _started;

    public Task StartAsync()
    {
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

    private StatusResult Status() => new(AppVersion.Current, Environment.ProcessId, _started, paths.Config, paths.State, options.StartedByApp);

    // .NET leaves the socket's permissions to the umask, so a private folder keeps other users from reaching it at all
    [UnsupportedOSPlatform("windows")]
    private void PrepareFolder()
    {
        var folder = EngineSocket.Folder(paths);
        Directory.CreateDirectory(paths.State);
        if (Directory.CreateDirectory(folder, Private).LinkTarget is not null)
        {
            throw new EngineException($"{folder} is a link, so the engine won't keep its socket there");
        }

        try
        {
            // It may have been made by something else first
            File.SetUnixFileMode(folder, Private);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new EngineException($"{folder} belongs to another user, so the engine can't keep its socket there", e);
        }

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
        catch (ArgumentOutOfRangeException e) when (first)
        {
            throw new EngineException($"The engine's socket, {_name}, would be a longer path than this OS allows. Use a --home folder with a shorter path", e);
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
            try
            {
                var connection = new Connection(Status);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                while (!connection.Closing && await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (connection.Answer(line) is { } response)
                    {
                        await pipe.WriteAsync(EngineProtocol.Line(response, ProtocolJson.Default.RpcResponse), cancellationToken);
                        await pipe.FlushAsync(cancellationToken);
                    }

                    if (connection.Stopping)
                    {
                        LogAskedToStop();
                        lifetime.StopApplication();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // It went away
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening at {Socket}")]
    private partial void LogListening(string socket);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping, as a client asked")]
    private partial void LogAskedToStop();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a connection from another user")]
    private partial void LogRefused();

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopped listening, so nothing more can connect")]
    private partial void LogStoppedListening(Exception exception);
}
