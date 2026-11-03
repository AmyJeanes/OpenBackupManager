using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// What the engine says to one client, a line at a time. It closes the connection once it's refused a client
internal sealed class Connection(Func<StatusResult> status, ConfigWatcher config, Secrets secrets)
{
    private bool _greeted;

    public bool Closing { get; private set; }

    // Once it's answered stop, which the engine then does
    public bool Stopping { get; private set; }

    // Null when nothing should be sent back: a message without an id is a notification, which JSON-RPC never answers
    public async Task<RpcResponse?> AnswerAsync(string line, CancellationToken cancellationToken)
    {
        RpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(line, ProtocolJson.Default.RpcRequest);
        }
        catch (JsonException)
        {
            return Error(null, RpcError.ParseError, "That isn't JSON");
        }

        if (request is not { JsonRpc: "2.0", Method: { } method })
        {
            return Error(request?.Id, RpcError.InvalidRequest, "That isn't a JSON-RPC 2.0 request");
        }

        if (request.Id is not { } id)
        {
            return null;
        }

        if (!_greeted && method != EngineProtocol.Hello)
        {
            Closing = true;
            return Error(id, RpcError.HelloFirst, $"Send {EngineProtocol.Hello} first");
        }

        return method switch
        {
            EngineProtocol.Hello => Hello(id, request.Params),
            EngineProtocol.Status => Result(id, status(), ProtocolJson.Default.StatusResult),
            EngineProtocol.Stop => Stop(id),
            EngineProtocol.MigrateSecrets => await MigrateSecretsAsync(id, request.Params, cancellationToken),
            EngineProtocol.ReloadConfig => ReloadConfig(id),
#if DEBUG
            EngineProtocol.Crash => Crash(id, request.Params),
#endif
            _ => Error(id, RpcError.MethodNotFound, $"There's no method called {method}"),
        };
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
        config.Reload();
        return Result(id, new ReloadConfigResult(config.Result.Problems), ProtocolJson.Default.ReloadConfigResult);
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
}
