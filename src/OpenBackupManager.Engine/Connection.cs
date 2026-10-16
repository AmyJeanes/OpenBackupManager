using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// What the engine says to one client, a line at a time. It closes the connection once it's refused a client
internal sealed class Connection(Func<StatusResult> status)
{
    private bool _greeted;

    public bool Closing { get; private set; }

    // Null when nothing should be sent back: a message without an id is a notification, which JSON-RPC never answers
    public RpcResponse? Answer(string line)
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
            _ => Error(id, RpcError.MethodNotFound, $"There's no method called {method}"),
        };
    }

    private RpcResponse Hello(JsonElement id, JsonElement? parameters)
    {
        HelloParams? hello;
        try
        {
            hello = parameters?.Deserialize(ProtocolJson.Default.HelloParams);
        }
        catch (JsonException)
        {
            hello = null;
        }

        if (hello is null)
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

    private static RpcResponse Result<T>(JsonElement id, T result, JsonTypeInfo<T> type) =>
        new("2.0", id, JsonSerializer.SerializeToElement(result, type), null);

    private static RpcResponse Error(JsonElement? id, int code, string message) => new("2.0", id, null, new RpcError(code, message));
}
