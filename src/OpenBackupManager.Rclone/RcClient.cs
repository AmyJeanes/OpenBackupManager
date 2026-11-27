using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenBackupManager.Rclone;

/// <summary>
/// Calls an rcd's rc API over its Unix socket, each method named as rclone names it
/// </summary>
public sealed class RcClient : IDisposable
{
    private readonly HttpClient _http;

    public RcClient(string socket)
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (_, cancellationToken) =>
            {
                var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), cancellationToken);
                    return new NetworkStream(connection, ownsSocket: true);
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            },
        })
        {
            // The socket decides where it goes, not the host
            BaseAddress = new Uri("http://rclone/"),
            // Calls such as a sign-in wait on the user, so each caller's token limits its own
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public Task<VersionResult> VersionAsync(CancellationToken cancellationToken) =>
        CallAsync("core/version", new NoParams(), RcJson.Default.NoParams, RcJson.Default.VersionResult, cancellationToken);

    public Task<ListRemotesResult> ListRemotesAsync(CancellationToken cancellationToken) =>
        CallAsync("config/listremotes", new NoParams(), RcJson.Default.NoParams, RcJson.Default.ListRemotesResult, cancellationToken);

    // Without it, rclone writes a new config in plain text
    public Task UnlockConfigAsync(string password, CancellationToken cancellationToken) =>
        CallAsync("config/unlock", new UnlockParams(password), RcJson.Default.UnlockParams, RcJson.Default.NoResult, cancellationToken);

    public Task<ProvidersResult> ProvidersAsync(CancellationToken cancellationToken) =>
        CallAsync("config/providers", new NoParams(), RcJson.Default.NoParams, RcJson.Default.ProvidersResult, cancellationToken);

    // Asking every question, not only those after the options, and each one back rather than at a terminal
    internal Task<ConfigStep> CreateConfigAsync(string name, string type, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) =>
        CallAsync("config/create", new CreateConfigParams(name, type, parameters, new ConfigOpt(NonInteractive: true, Obscure: true, All: true)),
            RcJson.Default.CreateConfigParams, RcJson.Default.ConfigStep, cancellationToken);

    // Parameters answer only the questions of the call they're in, so each passes them again
    internal Task<ConfigStep> ContinueConfigAsync(
        string name, IReadOnlyDictionary<string, string> parameters, string state, string result, CancellationToken cancellationToken) =>
        CallAsync("config/update", new UpdateConfigParams(name, parameters, new ConfigOpt(NonInteractive: true, Obscure: true, Continue: true, State: state, Result: result)),
            RcJson.Default.UpdateConfigParams, RcJson.Default.ConfigStep, cancellationToken);

    // As rclone keeps passwords in its config
    internal async Task<string> ObscureAsync(string clear, CancellationToken cancellationToken) =>
        (await CallAsync("core/obscure", new ObscureParams(clear), RcJson.Default.ObscureParams, RcJson.Default.ObscureResult, cancellationToken)).Obscured;

    // Of which only whether it ignores case is read
    public Task<FsInfoResult> FsInfoAsync(string fs, CancellationToken cancellationToken) =>
        CallAsync("operations/fsinfo", new FsParams(fs), RcJson.Default.FsParams, RcJson.Default.FsInfoResult, cancellationToken);

    // Only what's directly in it, or null when it isn't there
    public async Task<IReadOnlyList<ListItem>?> ListAsync(string fs, string remote, bool dirsOnly, CancellationToken cancellationToken)
    {
        try
        {
            return (await CallAsync(
                "operations/list", new ItemParams(fs, remote, new ItemOpt(dirsOnly)), RcJson.Default.ItemParams, RcJson.Default.ListResult, cancellationToken)).List;
        }
        catch (RcloneException e) when (e.NotFound)
        {
            return null;
        }
    }

    // Null when nothing's there
    public async Task<ListItem?> StatAsync(string fs, string remote, CancellationToken cancellationToken) =>
        (await CallAsync("operations/stat", new ItemParams(fs, remote, new ItemOpt()), RcJson.Default.ItemParams, RcJson.Default.StatResult, cancellationToken)).Item;

    // With any folders it's in
    public Task MkdirAsync(string fs, string remote, CancellationToken cancellationToken) =>
        CallAsync("operations/mkdir", new PathParams(fs, remote), RcJson.Default.PathParams, RcJson.Default.NoResult, cancellationToken);

    // Into the folder remote, as name
    public async Task UploadFileAsync(string fs, string remote, string name, byte[] content, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", name } };
        var method = "operations/uploadfile";
        using var response = await PostAsync($"{method}?fs={Uri.EscapeDataString(fs)}&remote={Uri.EscapeDataString(remote)}", method, form, cancellationToken);
        await ReadAsync(method, response, RcJson.Default.NoResult, cancellationToken);
    }

    public Task DeleteFileAsync(string fs, string remote, CancellationToken cancellationToken) =>
        CallAsync("operations/deletefile", new PathParams(fs, remote), RcJson.Default.PathParams, RcJson.Default.NoResult, cancellationToken);

    public Task<PidResult> PidAsync(CancellationToken cancellationToken) =>
        CallAsync("core/pid", new NoParams(), RcJson.Default.NoParams, RcJson.Default.PidResult, cancellationToken);

    // rcd exits 1.5 s after answering
    public Task QuitAsync(CancellationToken cancellationToken) =>
        CallAsync("core/quit", new NoParams(), RcJson.Default.NoParams, RcJson.Default.NoResult, cancellationToken);

    public void Dispose() => _http.Dispose();

    internal async Task<TResult> CallAsync<TParams, TResult>(
        string method, TParams parameters, JsonTypeInfo<TParams> paramsType, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(method, method, JsonContent.Create(parameters, paramsType), cancellationToken);
        return await ReadAsync(method, response, resultType, cancellationToken);
    }

    private static async Task<TResult> ReadAsync<TResult>(
        string method, HttpResponseMessage response, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Only its message, as rclone sends the call's parameters back with it, which can hold a password
            var error = await response.Content.ReadFromJsonAsync(RcJson.Default.RcError, cancellationToken);
            throw new RcloneException($"rclone's {method} failed: {error?.Error ?? response.ReasonPhrase}")
            {
                NotFound = response.StatusCode == HttpStatusCode.NotFound,
            };
        }

        return await response.Content.ReadFromJsonAsync(resultType, cancellationToken)
            ?? throw new RcloneException($"rclone's {method} answered with nothing");
    }

    // HttpClient doesn't wrap the SocketException macOS gives when rcd goes just as it connects
    private async Task<HttpResponseMessage> PostAsync(string path, string method, HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.PostAsync(path, content, cancellationToken);
        }
        catch (SocketException e)
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError, $"Couldn't reach rclone for {method}: {e.Message}", e);
        }
    }
}

public sealed record VersionResult(string Version);

public sealed record PidResult(int Pid);

public sealed record ListRemotesResult(IReadOnlyList<string>? Remotes);

public sealed record FsInfoResult(FsFeatures Features);

public sealed record FsFeatures(bool CaseInsensitive, bool IsLocal);

// Path is from the root of the remote it was listed through, in the case it was asked for, while Name is in its own
public sealed record ListItem(string Path, string Name, long Size, bool IsDir);

internal sealed record FsParams(string Fs);

internal sealed record PathParams(string Fs, string Remote);

internal sealed record ItemParams(string Fs, string Remote, ItemOpt Opt);

internal sealed record ItemOpt(bool DirsOnly = false, bool NoModTime = true, bool NoMimeType = true);

internal sealed record ListResult(IReadOnlyList<ListItem> List);

internal sealed record StatResult(ListItem? Item);

public sealed record ProvidersResult(IReadOnlyList<Provider> Providers);

// Prefix is what a remote's type takes, such as gcs for Google Cloud Storage, whose name is google cloud storage. Hide
// marks an old name for another
public sealed record Provider(string Name, string Prefix, string Description, bool Hide, ProviderOverview Overview);

// rclone's own rating of a backend, as on rclone.org/tiers: its tier, such as "Tier 1", whether it wraps another
// remote, such as crypt, and how its integration tests went, such as "Passing", or "N/A" where they can't run
public sealed record ProviderOverview(string Tier, bool Virtual, string IntegrationTests);

internal sealed record UnlockParams(string ConfigPassword);

internal sealed record CreateConfigParams(string Name, string Type, IReadOnlyDictionary<string, string> Parameters, ConfigOpt Opt);

internal sealed record UpdateConfigParams(string Name, IReadOnlyDictionary<string, string> Parameters, ConfigOpt Opt);

// Obscure is for passwords among the parameters, as otherwise rclone leaves one that looks obscured as it is. Not
// for the result, which is saved as given
internal sealed record ConfigOpt(bool NonInteractive, bool Obscure, bool All = false, bool Continue = false, string? State = null, string? Result = null);

// Where it goes next, with the question to ask there, if any. Done once State is empty
internal sealed record ConfigStep(string? State, ConfigOption? Option, string? Error, string? Result);

// Default is null for none, which DefaultStr says as <nil>
internal sealed record ConfigOption(
    string Name,
    string? Help,
    JsonElement Default,
    string? DefaultStr,
    IReadOnlyList<ConfigExample>? Examples,
    bool Required,
    bool IsPassword,
    bool Exclusive,
    string? Type);

internal sealed record ConfigExample(string Value, string? Help);

internal sealed record ObscureParams(string Clear);

internal sealed record ObscureResult(string Obscured);

internal sealed record NoParams;

internal sealed record NoResult;

internal sealed record RcError(string? Error);

// Case-insensitive, as rclone answers config steps with Go's own field names, such as State
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CreateConfigParams))]
[JsonSerializable(typeof(UpdateConfigParams))]
[JsonSerializable(typeof(ConfigStep))]
[JsonSerializable(typeof(ObscureParams))]
[JsonSerializable(typeof(ObscureResult))]
[JsonSerializable(typeof(NoParams))]
[JsonSerializable(typeof(NoResult))]
[JsonSerializable(typeof(PidResult))]
[JsonSerializable(typeof(ListRemotesResult))]
[JsonSerializable(typeof(ProvidersResult))]
[JsonSerializable(typeof(FsParams))]
[JsonSerializable(typeof(FsInfoResult))]
[JsonSerializable(typeof(PathParams))]
[JsonSerializable(typeof(ItemParams))]
[JsonSerializable(typeof(ListResult))]
[JsonSerializable(typeof(StatResult))]
[JsonSerializable(typeof(UnlockParams))]
[JsonSerializable(typeof(RcError))]
[JsonSerializable(typeof(VersionResult))]
internal sealed partial class RcJson : JsonSerializerContext;
