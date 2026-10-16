using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenBackupManager.Core.Protocol;

// JSON-RPC 2.0, one message to a line, so programs in any language can talk to the engine. The client sends hello
// first, and the engine refuses a client whose protocol isn't its own
public static class EngineProtocol
{
    // Goes up when a change would be misread by the other side
    public const int Version = 1;

    public const string Hello = "hello";
    public const string Status = "status";

    public static byte[] Line<T>(T message, JsonTypeInfo<T> type) => [.. JsonSerializer.SerializeToUtf8Bytes(message, type), (byte)'\n'];
}

// Anything may be missing from what a client sends, which the engine answers with an error
public sealed record RpcRequest(
    [property: JsonPropertyName("jsonrpc")] string? JsonRpc = null,
    JsonElement? Id = null,
    string? Method = null,
    JsonElement? Params = null);

public sealed record RpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    // Written even when null, as JSON-RPC requires, where ProtocolJson would leave it out
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Id,
    JsonElement? Result = null,
    RpcError? Error = null);

public sealed record RpcError(int Code, string Message)
{
    // JSON-RPC's own codes
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;

    // Ours, from the range JSON-RPC leaves for servers
    public const int ProtocolMismatch = -32000;
    public const int HelloFirst = -32001;
}

public sealed record HelloParams(int Protocol, string Version);

public sealed record HelloResult(int Protocol, string Version);

public sealed record StatusResult(string Version, int ProcessId, DateTimeOffset Started, string Config, string State);

// Nullable annotations are kept to, so a message missing what it needs isn't read
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(RpcResponse))]
[JsonSerializable(typeof(HelloParams))]
[JsonSerializable(typeof(HelloResult))]
[JsonSerializable(typeof(StatusResult))]
public sealed partial class ProtocolJson : JsonSerializerContext;
