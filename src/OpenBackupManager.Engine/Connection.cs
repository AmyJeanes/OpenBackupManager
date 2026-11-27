using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// What the engine says to one client, a line at a time. It closes the connection once it's refused a client, and
// ends the setups it started as it closes
internal sealed partial class Connection(Func<StatusResult> status, ConfigWatcher config, Secrets secrets, Remotes remotes, Setups setups, Backends backends, ILogger logger)
    : IAsyncDisposable
{
    private readonly Dictionary<string, Setup> _setups = [];
    private bool _greeted;

    // The calls read but not yet answered, by their ids as JSON, so cancelRequest can stop one
    private readonly Lock _cancelling = new();
    private readonly Dictionary<string, CancellationTokenSource> _calls = [];

    public bool Closing { get; private set; }

    // Once it's answered stop, which the engine then does
    public bool Stopping { get; private set; }

    // As each line arrives, while an earlier call may still be being answered, so cancelRequest is acted on at once
    public Received Receive(string line)
    {
        RpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(line, ProtocolJson.Default.RpcRequest);
        }
        catch (JsonException)
        {
            return new Received(Unreadable: true);
        }

        if (request is { JsonRpc: "2.0", Method: { } method })
        {
            if (request.Id is { } id)
            {
                var cancel = new CancellationTokenSource();
                lock (_cancelling)
                {
                    _calls[id.GetRawText()] = cancel;
                }

                return new Received(request, cancel);
            }

            if (method == EngineProtocol.CancelRequest && Params(request.Params, ProtocolJson.Default.CancelRequestParams) is { } cancelRequest)
            {
                lock (_cancelling)
                {
                    if (_calls.TryGetValue(cancelRequest.Id.GetRawText(), out var call))
                    {
                        call.Cancel();
                    }
                }
            }
        }

        return new Received(request);
    }

    // Null when nothing should be sent back: a message without an id is a notification, which JSON-RPC never answers
    public async Task<RpcResponse?> AnswerAsync(Received received, CancellationToken cancellationToken)
    {
        if (received.Unreadable)
        {
            return Error(null, RpcError.ParseError, "That isn't JSON");
        }

        if (received.Request is not { JsonRpc: "2.0", Method: { } method } request)
        {
            return Error(received.Request?.Id, RpcError.InvalidRequest, "That isn't a JSON-RPC 2.0 request");
        }

        if (request.Id is not { } id || received.Cancel is not { } cancel)
        {
            return null;
        }

        try
        {
            using var answering = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancel.Token);
            return await AnswerAsync(id, method, request.Params, answering.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Error(id, RpcError.Cancelled, $"{method} was cancelled");
        }
        finally
        {
            lock (_cancelling)
            {
                var key = id.GetRawText();
                if (_calls.TryGetValue(key, out var call) && call == cancel)
                {
                    _calls.Remove(key);
                }
            }

            cancel.Dispose();
        }
    }

    private async Task<RpcResponse> AnswerAsync(JsonElement id, string method, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (!_greeted && method != EngineProtocol.Hello)
        {
            Closing = true;
            return Error(id, RpcError.HelloFirst, $"Send {EngineProtocol.Hello} first");
        }

        try
        {
            return method switch
            {
                EngineProtocol.Hello => Hello(id, parameters),
                EngineProtocol.Status => Result(id, status(), ProtocolJson.Default.StatusResult),
                EngineProtocol.Stop => Stop(id),
                EngineProtocol.MigrateSecrets => await MigrateSecretsAsync(id, parameters, cancellationToken),
                EngineProtocol.ReloadConfig => ReloadConfig(id),
                EngineProtocol.RetryRemote => RetryRemote(id, parameters),
                EngineProtocol.ListBackends => Result(id, new ListBackendsResult(await backends.ListAsync(cancellationToken)), ProtocolJson.Default.ListBackendsResult),
                EngineProtocol.SetupRemote => await SetupRemoteAsync(id, parameters, cancellationToken),
                EngineProtocol.AnswerSetup => await AnswerSetupAsync(id, parameters, cancellationToken),
                EngineProtocol.ListFolders => await ListFoldersAsync(id, parameters, cancellationToken),
                EngineProtocol.CancelSetup => await CancelSetupAsync(id, parameters),
#if DEBUG
                EngineProtocol.Crash => Crash(id, parameters),
#endif
                _ => Error(id, RpcError.MethodNotFound, $"There's no method called {method}"),
            };
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Answered rather than left to close the connection, which would also make stopping the engine fail
            LogFailed(logger, method, e);
            return Error(id, RpcError.Failed, e.Message);
        }
    }

    private RpcResponse Hello(JsonElement id, JsonElement? parameters)
    {
        if (Params(parameters, ProtocolJson.Default.HelloParams) is not { } hello)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.Hello} needs the client's protocol and version");
        }

        if (hello.Protocol != EngineProtocol.Version)
        {
            Closing = true;
            return Error(
                id,
                RpcError.ProtocolMismatch,
                $"The engine running is version {AppVersion.Current}, which can't work with version {hello.Version}. Restart it to run the same version");
        }

        _greeted = true;
        return Result(id, new HelloResult(EngineProtocol.Version, AppVersion.Current), ProtocolJson.Default.HelloResult);
    }

    // Answered once it's done, which takes as long as the stores do
    private async Task<RpcResponse> MigrateSecretsAsync(JsonElement id, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (Params(parameters, ProtocolJson.Default.MigrateSecretsParams) is not { } migrate)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.MigrateSecrets} needs the store to move the secrets to");
        }

        try
        {
            return Result(id, await secrets.MigrateAsync(migrate.Store, cancellationToken), ProtocolJson.Default.MigrateSecretsResult);
        }
        catch (EngineException e)
        {
            return Error(id, RpcError.Failed, e.Message);
        }
    }

    private RpcResponse ReloadConfig(JsonElement id)
    {
        try
        {
            config.Reload();
        }
        catch (ConfigException e)
        {
            return Error(id, RpcError.Failed, e.Message + ", so the last good settings stay in use");
        }

        return Result(id, new ReloadConfigResult(config.Result.Problems), ProtocolJson.Default.ReloadConfigResult);
    }

    private RpcResponse RetryRemote(JsonElement id, JsonElement? parameters)
    {
        if (Params(parameters, ProtocolJson.Default.RetryRemoteParams) is not { } retry)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.RetryRemote} needs the remote's id");
        }

        try
        {
            remotes.Retry(retry.Id);
        }
        catch (EngineException e)
        {
            return Error(id, RpcError.Failed, e.Message);
        }

        return Result(id, new RetryRemoteResult(), ProtocolJson.Default.RetryRemoteResult);
    }

    private async Task<RpcResponse> SetupRemoteAsync(JsonElement id, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (Params(parameters, ProtocolJson.Default.SetupRemoteParams) is not { } start)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.SetupRemote} needs the remote's id and its rclone type");
        }

        Setup setup;
        try
        {
            setup = await setups.StartAsync(start, cancellationToken);
        }
        catch (EngineException e)
        {
            return Error(id, e.Problem == EngineProblem.NoConfigPassword ? RpcError.NoConfigPassword : RpcError.Failed, e.Message);
        }

        if (setup.Question is not null)
        {
            _setups[start.Id] = setup;
        }

        return Result(id, new SetupStep(setup.Question), ProtocolJson.Default.SetupStep);
    }

    // A step that fails or is cancelled ends it, as rclone's own setup can't be told to go back
    private async Task<RpcResponse> AnswerSetupAsync(JsonElement id, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (Params(parameters, ProtocolJson.Default.AnswerSetupParams) is not { } answer)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.AnswerSetup} needs the remote's id and the answer");
        }

        if (!_setups.TryGetValue(answer.Id, out var setup))
        {
            return Error(id, RpcError.Failed, $"There's no setup of {answer.Id} waiting for an answer here. It may have finished or been cancelled, or another program may have started it");
        }

        try
        {
            await setup.AnswerAsync(answer.Answer, cancellationToken);
        }
        catch (EngineException e)
        {
            await EndAsync(setup);
            return Error(id, RpcError.Failed, e.Message);
        }
        catch (OperationCanceledException)
        {
            await EndAsync(setup);
            throw;
        }

        if (setup.Question is null)
        {
            _setups.Remove(answer.Id);
        }

        return Result(id, new SetupStep(setup.Question), ProtocolJson.Default.SetupStep);
    }

    // One that fails leaves the setup where it was
    private async Task<RpcResponse> ListFoldersAsync(JsonElement id, JsonElement? parameters, CancellationToken cancellationToken)
    {
        if (Params(parameters, ProtocolJson.Default.ListFoldersParams) is not { } list)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.ListFolders} needs the remote's id and a path");
        }

        if (!_setups.TryGetValue(list.Id, out var setup))
        {
            return Error(id, RpcError.Failed, $"There's no setup of {list.Id} here. It may have finished or been cancelled, or another program may have started it");
        }

        try
        {
            var found = await setup.ListFoldersAsync(list.Path, cancellationToken);
            return Result(id, new ListFoldersResult(found.Path, found.Folders!), ProtocolJson.Default.ListFoldersResult);
        }
        catch (EngineException e)
        {
            return Error(id, RpcError.Failed, e.Message);
        }
    }

    private async Task<RpcResponse> CancelSetupAsync(JsonElement id, JsonElement? parameters)
    {
        if (Params(parameters, ProtocolJson.Default.CancelSetupParams) is not { } cancel)
        {
            return Error(id, RpcError.InvalidParams, $"{EngineProtocol.CancelSetup} needs the remote's id");
        }

        if (_setups.TryGetValue(cancel.Id, out var setup))
        {
            await EndAsync(setup);
        }

        return Result(id, new CancelSetupResult(), ProtocolJson.Default.CancelSetupResult);
    }

    private async Task EndAsync(Setup setup)
    {
        _setups.Remove(setup.Id);
        await setup.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var setup in _setups.Values)
        {
            await setup.DisposeAsync();
        }

        _setups.Clear();
    }

    private RpcResponse Stop(JsonElement id)
    {
        Stopping = Closing = true;
        return Result(id, new StopResult(), ProtocolJson.Default.StopResult);
    }

#if DEBUG
    // A moment after replying, so the reply reaches the test first
    private static RpcResponse Crash(JsonElement id, JsonElement? parameters)
    {
        var how = parameters?.Deserialize(ProtocolJson.Default.CrashParams)?.How;
        new Thread(() =>
        {
            Thread.Sleep(200);
            if (how == CrashParams.FailFast)
            {
                Environment.FailFast("Test crash that fails fast");
            }

            throw new InvalidOperationException("Test crash on another thread");
        }).Start();
        return Result(id, new CrashResult(), ProtocolJson.Default.CrashResult);
    }
#endif

    // A line as it was read. A call has its own token, which cancelRequest cancels
    internal sealed record Received(RpcRequest? Request = null, CancellationTokenSource? Cancel = null, bool Unreadable = false);

    // Null when they're missing or the wrong shape
    private static T? Params<T>(JsonElement? parameters, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return parameters?.Deserialize(type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RpcResponse Result<T>(JsonElement id, T result, JsonTypeInfo<T> type) =>
        new("2.0", id, JsonSerializer.SerializeToElement(result, type), null);

    private static RpcResponse Error(JsonElement? id, int code, string message) => new("2.0", id, null, new RpcError(code, message));

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't answer {Method}")]
    private static partial void LogFailed(ILogger logger, string method, Exception exception);
}
