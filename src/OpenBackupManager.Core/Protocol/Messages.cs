using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenBackupManager.Core.Protocol;

// JSON-RPC 2.0, one message to a line, so programs in any language can talk to the engine. The client sends hello
// first, and the engine refuses a client whose protocol isn't its own
public static class EngineProtocol
{
    // Goes up when a change would be misread by the other side, once there are releases people use. Until then,
    // changes are made in place
    public const int Version = 1;

    public const string Hello = "hello";
    public const string Status = "status";
    public const string Stop = "stop";

#if DEBUG
    // For testing crash reports
    public const string Crash = "crash";
#endif

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

    // Ours, from the range JSON-RPC leaves for servers. The app words some itself, so a code keeps its meaning,
    // `hello` and this reply keep their shape in every version, as they're how two different versions find out
    public const int ProtocolMismatch = -32000;
    public const int HelloFirst = -32001;
}

public sealed record HelloParams(int Protocol, string Version);

public sealed record HelloResult(int Protocol, string Version);

public sealed record StatusResult(string Version, int ProcessId, DateTimeOffset Started, string Config, string State, EngineStarter StartedBy);

// Quit in the tray app stops only an engine the app started
[JsonConverter(typeof(JsonStringEnumConverter<EngineStarter>))]
public enum EngineStarter
{
    // By hand, or by something the user set up themselves, such as Task Scheduler
    [JsonStringEnumMemberName("other")]
    Other,

    [JsonStringEnumMemberName("app")]
    App,

    // By the OS's service manager, from obm service install
    [JsonStringEnumMemberName("service")]
    Service,
}

// The engine replies, then stops, closing every connection
public sealed record StopResult;

#if DEBUG
// The engine replies, then crashes as it's asked
public sealed record CrashParams(string How)
{
    public const string OnAnotherThread = "on-another-thread";

    // Skips .NET's handlers, as a crash inside Windows' own code would
    public const string FailFast = "fail-fast";
}

public sealed record CrashResult;
#endif

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
[JsonSerializable(typeof(StopResult))]
#if DEBUG
[JsonSerializable(typeof(CrashParams))]
[JsonSerializable(typeof(CrashResult))]
#endif
public sealed partial class ProtocolJson : JsonSerializerContext;
