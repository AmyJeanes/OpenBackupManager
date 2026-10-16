using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenBackupManager.Core.Protocol;

/// <summary>A connection to the engine for a home</summary>
public sealed class EngineClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly SemaphoreSlim _calling = new(1, 1);
    private int _lastId;

    private EngineClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    }

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
            throw new EngineException("The engine running for this home was started by another user or as administrator, so it can't be reached from here", e);
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

    public void Dispose()
    {
        _reader.Dispose();
        _pipe.Dispose();
        _calling.Dispose();
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
        await _calling.WaitAsync(cancellationToken);
        try
        {
            var id = ++_lastId;
            var request = new RpcRequest("2.0", JsonSerializer.SerializeToElement(id, ProtocolJson.Default.Int32), method, parameters);
            await _pipe.WriteAsync(EngineProtocol.Line(request, ProtocolJson.Default.RpcRequest), cancellationToken);
            await _pipe.FlushAsync(cancellationToken);

            var line = await _reader.ReadLineAsync(cancellationToken) ?? throw new EngineException("The engine closed the connection");
            var response = JsonSerializer.Deserialize(line, ProtocolJson.Default.RpcResponse)
                ?? throw new EngineException("The engine sent an empty reply");
            if (response.Error is { } error)
            {
                throw new EngineException(error.Message);
            }

            return response.Result is { } result && result.Deserialize(resultType) is { } value
                ? value
                : throw new EngineException($"The engine didn't answer {method}");
        }
        finally
        {
            _calling.Release();
        }
    }
}
