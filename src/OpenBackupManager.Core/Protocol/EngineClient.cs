using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenBackupManager.Core.Protocol;

/// <summary>A connection to the engine for a home. Calls can overlap, each answered by its own id</summary>
public sealed class EngineClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RpcResponse>> _waiting = new();
    private int _lastId;

    private EngineClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        Closed = ReadAsync();
    }

    // Completes once the connection ends, such as when the engine stops or crashes
    public Task Closed { get; }

    // Null if no engine is running, which takes the whole timeout to find out, as one may be starting
    public static async Task<EngineClient?> ConnectAsync(AppPaths paths, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // .NET's own check on Windows expects the pipe to belong to whoever owns what this process makes, which when it's
        // elevated is Administrators rather than the user, so obm in an admin terminal couldn't reach the engine
        var options = PipeOptions.Asynchronous | (OperatingSystem.IsWindows() ? PipeOptions.None : PipeOptions.CurrentUserOnly);
        var pipe = new NamedPipeClientStream(".", EngineSocket.Name(paths), PipeDirection.InOut, options);
        try
        {
            await pipe.ConnectAsync(timeout, cancellationToken);
            if (OperatingSystem.IsWindows() && !OwnedByThisUser(pipe))
            {
                throw new UnauthorizedAccessException();
            }
        }
        // Thrown for a socket path too long for the OS, where the engine can't have started either
        catch (Exception e) when (e is TimeoutException or ArgumentOutOfRangeException)
        {
            await pipe.DisposeAsync();
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            await pipe.DisposeAsync();
            throw new EngineException(
                EngineProblem.AnotherUserOrAdmin,
                "The engine running for this home was started by another user or as administrator, so it can't be reached from here",
                e);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }

        var client = new EngineClient(pipe);
        try
        {
            var hello = new HelloParams(EngineProtocol.Version, AppVersion.Current);
            await client.CallAsync(
                EngineProtocol.Hello,
                JsonSerializer.SerializeToElement(hello, ProtocolJson.Default.HelloParams),
                ProtocolJson.Default.HelloResult,
                cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    public Task<StatusResult> StatusAsync(CancellationToken cancellationToken) =>
        CallAsync(EngineProtocol.Status, null, ProtocolJson.Default.StatusResult, cancellationToken);

    // Returns once the engine has closed the connection, as it does as it stops
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await CallAsync(EngineProtocol.Stop, null, ProtocolJson.Default.StopResult, cancellationToken);
        await Closed.WaitAsync(cancellationToken);
    }

#if DEBUG
    // Only debug builds of the engine have it
    public Task CrashAsync(string how, CancellationToken cancellationToken) =>
        CallAsync(EngineProtocol.Crash, JsonSerializer.SerializeToElement(new CrashParams(how), ProtocolJson.Default.CrashParams), ProtocolJson.Default.CrashResult, cancellationToken);
#endif

    public void Dispose()
    {
        _pipe.Dispose();
        _reader.Dispose();
        _writing.Dispose();
    }

    // Either way it's this user's: an engine that's elevated too makes its pipe belong to Administrators
    [SupportedOSPlatform("windows")]
    private static bool OwnedByThisUser(NamedPipeClientStream pipe)
    {
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        using var me = WindowsIdentity.GetCurrent();
        return owner == me.User || owner == me.Owner;
    }

    private async Task<TResult> CallAsync<TResult>(
        string method,
        JsonElement? parameters,
        JsonTypeInfo<TResult> resultType,
        CancellationToken cancellationToken)
        where TResult : class
    {
        var id = Interlocked.Increment(ref _lastId);
        var answer = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = answer;
        try
        {
            var request = new RpcRequest("2.0", JsonSerializer.SerializeToElement(id, ProtocolJson.Default.Int32), method, parameters);
            await _writing.WaitAsync(cancellationToken);
            try
            {
                await _pipe.WriteAsync(EngineProtocol.Line(request, ProtocolJson.Default.RpcRequest), cancellationToken);
                await _pipe.FlushAsync(cancellationToken);
            }
            catch (IOException e)
            {
                throw new EngineException("The engine closed the connection", e);
            }
            finally
            {
                _writing.Release();
            }

            // The connection may have ended before this call was waiting, so its end wasn't passed on to it
            await Task.WhenAny(answer.Task, Closed).WaitAsync(cancellationToken);
            var response = answer.Task.IsCompleted ? await answer.Task : throw new EngineException("The engine closed the connection");
            if (response.Error is { } error)
            {
                throw error.Code == RpcError.ProtocolMismatch
                    ? new EngineException(EngineProblem.AnotherVersion, error.Message)
                    : new EngineException(error.Message);
            }

            return response.Result is { } result && result.Deserialize(resultType) is { } value
                ? value
                : throw new EngineException($"The engine didn't answer {method}");
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    // Hands each reply to the call with its id. Whatever ends the connection, every call still waiting is told
    private async Task ReadAsync()
    {
        try
        {
            while (await _reader.ReadLineAsync() is { } line)
            {
                var response = JsonSerializer.Deserialize(line, ProtocolJson.Default.RpcResponse);
                if (response?.Id is { ValueKind: JsonValueKind.Number } id && id.TryGetInt32(out var number) && _waiting.TryGetValue(number, out var answer))
                {
                    answer.TrySetResult(response);
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or JsonException)
        {
        }

        foreach (var answer in _waiting.Values)
        {
            answer.TrySetException(new EngineException("The engine closed the connection"));
        }
    }
}
