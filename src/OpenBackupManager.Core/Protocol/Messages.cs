using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.Core.Protocol;

// JSON-RPC 2.0, one message to a line, so programs in any language can talk to the engine. The client sends hello
// first, and the engine refuses a client whose protocol isn't its own
public static class EngineProtocol
{
    // Goes up when a change would be misread by the other side
    public const int Version = 1;

    public const string Hello = "hello";
    public const string Status = "status";
    public const string Stop = "stop";
    public const string MigrateSecrets = "migrateSecrets";
    public const string ReloadConfig = "reloadConfig";
    public const string RetryRemote = "retryRemote";
    public const string ListBackends = "listBackends";
    public const string SetupRemote = "setupRemote";
    public const string ListFolders = "listFolders";
    public const string AnswerSetup = "answerSetup";
    public const string CancelSetup = "cancelSetup";

    // LSP's, as StreamJsonRpc's is too. A notification, so it's never answered, but the call it names is, as cancelled
    public const string CancelRequest = "$/cancelRequest";

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Id = null,
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

    // It couldn't do what was asked, and the message says why
    public const int Failed = -32002;

    // A remote can't be set up without a new config password, which would leave the others' configs unreadable, so
    // the person chooses whether to start afresh, setting them all up again
    public const int NoConfigPassword = -32003;

    // LSP's, with $/cancelRequest
    public const int Cancelled = -32800;
}

public sealed record HelloParams(int Protocol, string Version);

public sealed record HelloResult(int Protocol, string Version);

public sealed record StatusResult(
    string Version,
    int ProcessId,
    DateTimeOffset Started,
    string Config,
    string State,
    EngineStarter StartedBy,
    SecretsStatus Secrets,
    // From the engine's latest read of config.toml. An error leaves the last good settings in use
    IReadOnlyList<ConfigProblem> ConfigProblems,
    // In config.toml's order
    IReadOnlyList<RemoteStatus> Remotes,
    // The version an update installed over the engine's own, which runs once the engine is restarted
    string? Installed = null);

// Store is the store in use, as secret-store names it, or null when none is chosen yet. File is the file store's path.
// FromEnvironment names the variables that give secrets instead, such as OBM_RCLONE_CONFIG_SECRET_FILE
public sealed record SecretsStatus(string? Store = null, string? File = null, IReadOnlyList<string>? FromEnvironment = null);

public sealed record MigrateSecretsParams(string Store);

// From is the store they were in, as secret-store names it, or null when none was chosen. When it's the store asked
// for, nothing was moved. Left names the secrets that couldn't be removed from it. File is the file store's path
public sealed record MigrateSecretsResult(int Moved, IReadOnlyList<string> Left, string File, string? From = null);

// Starts a remote again that's stopped trying, or tries now one that's waiting to
public sealed record RetryRemoteParams(string Id);

public sealed record RetryRemoteResult;

// The backends a remote can be set up with, those Open Backup Manager is tested with first
public sealed record ListBackendsResult(IReadOnlyList<BackendInfo> Backends);

// Type is what setupRemote takes, and the description is rclone's, in English. Tier is rclone's own rating, from 1
// (Core) to 4 (Experimental), as on rclone.org/tiers. Docs is rclone's page about it
public sealed record BackendInfo(string Type, string Description, bool Tested, int Tier, Uri Docs);

// Sets a remote up through rclone's own questions, or one that needs setting up again. Parameters answer any question
// by its name, over the engine's preset for the backend, such as someone's own client ID. A new one is then asked for
// its root, and the name is only for a new one, which is added to config.toml once it's set up
public sealed record SetupRemoteParams(
    string Id,
    string Type,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    bool StartAfresh = false);

public sealed record AnswerSetupParams(string Id, string Answer);

// The next question, or none once it's set up
public sealed record SetupStep(SetupQuestion? Question = null);

// One of rclone's questions, in its own words, which are only in English. Choices are the answers it suggests, and
// Exclusive says it has to be one of them. Error is what rclone said about the step before, if anything
public sealed record SetupQuestion(
    string Name,
    string Help,
    string Default,
    IReadOnlyList<SetupChoice> Choices,
    bool Required,
    bool IsPassword,
    bool Exclusive,
    string Type,
    string? Error = null,
    RootFolder? Folder = null)
{
    // Ours, after rclone's, which clients word themselves, as no rclone question's name starts with obm-. The root is
    // the path inside the remote, as rclone takes it, such as OpenBackupManager/, or empty for its top. listFolders
    // helps choose it
    public const string Root = "obm-root";

    // Whether to use the root chosen, yes or no, with what's there in Folder. No asks for the root again
    public const string RootConfirm = "obm-root-confirm";
}

// Path is the root in the remote's own case. NearMiss is a folder that's there, for one that isn't, that it may have
// been meant to be. AboveUnlisted is when a folder above couldn't be listed, so a backup there, which would be refused,
// or the case of its name, couldn't be checked
public sealed record RootFolder(string Path, RootFolderState State, int Folders, int Files, string? NearMiss = null, bool AboveUnlisted = false);

public enum RootFolderState
{
    // Made if it's used
    Missing,
    Empty,

    // None of them Open Backup Manager's, which are left alone beside its own
    HasFiles,
}

public sealed record SetupChoice(string Value, string Help);

// The folders at a path on a remote being set up, once it's signed in. The path comes back in the remote's own case
public sealed record ListFoldersParams(string Id, string Path);

public sealed record ListFoldersResult(string Path, IReadOnlyList<string> Folders);

// Leaves a remote that needs setting up again as it was
public sealed record CancelSetupParams(string Id);

public sealed record CancelSetupResult;

// Stops a call this client is waiting on, by its id, whether it's being answered or waiting its turn
public sealed record CancelRequestParams(JsonElement Id);

// What reading config.toml again found, as status gives it
public sealed record ReloadConfigResult(IReadOnlyList<ConfigProblem> Problems);

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

// Nullable annotations are kept to, so a message missing what it needs isn't read. Nulls are left out, so each nullable
// parameter defaults to null, or a message holding one couldn't be read
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    Converters =
    [
        typeof(CamelCaseEnumConverter<ConfigProblemKind>),
        typeof(CamelCaseEnumConverter<RemoteState>),
        typeof(CamelCaseEnumConverter<RemoteReason>),
        typeof(CamelCaseEnumConverter<RootFolderState>),
    ])]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(RpcResponse))]
[JsonSerializable(typeof(HelloParams))]
[JsonSerializable(typeof(HelloResult))]
[JsonSerializable(typeof(StatusResult))]
[JsonSerializable(typeof(SecretsStatus))]
[JsonSerializable(typeof(StopResult))]
[JsonSerializable(typeof(MigrateSecretsParams))]
[JsonSerializable(typeof(MigrateSecretsResult))]
[JsonSerializable(typeof(ReloadConfigResult))]
[JsonSerializable(typeof(RetryRemoteParams))]
[JsonSerializable(typeof(RetryRemoteResult))]
[JsonSerializable(typeof(ListBackendsResult))]
[JsonSerializable(typeof(SetupRemoteParams))]
[JsonSerializable(typeof(ListFoldersParams))]
[JsonSerializable(typeof(ListFoldersResult))]
[JsonSerializable(typeof(AnswerSetupParams))]
[JsonSerializable(typeof(SetupStep))]
[JsonSerializable(typeof(CancelSetupParams))]
[JsonSerializable(typeof(CancelSetupResult))]
[JsonSerializable(typeof(CancelRequestParams))]
#if DEBUG
[JsonSerializable(typeof(CrashParams))]
[JsonSerializable(typeof(CrashResult))]
#endif
public sealed partial class ProtocolJson : JsonSerializerContext;

// By name, as a number would change meaning when a value's added before it
public sealed class CamelCaseEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.CamelCase)
    where T : struct, Enum;
