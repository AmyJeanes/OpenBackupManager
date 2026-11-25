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

    // Asking every question, not only those after the options, and each one back rather than at a terminal
    internal Task<ConfigStep> CreateConfigAsync(string name, string type, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) =>
        CallAsync("config/create", new CreateConfigParams(name, type, parameters, new ConfigOpt(NonInteractive: true, All: true)),
            RcJson.Default.CreateConfigParams, RcJson.Default.ConfigStep, cancellationToken);

    // Parameters answer only the questions of the call they're in, so each passes them again
    internal Task<ConfigStep> ContinueConfigAsync(
        string name, IReadOnlyDictionary<string, string> parameters, string state, string result, CancellationToken cancellationToken) =>
        CallAsync("config/update", new UpdateConfigParams(name, parameters, new ConfigOpt(NonInteractive: true, Continue: true, State: state, Result: result)),
            RcJson.Default.UpdateConfigParams, RcJson.Default.ConfigStep, cancellationToken);

    // As rclone keeps passwords in its config
    internal async Task<string> ObscureAsync(string clear, CancellationToken cancellationToken) =>
        (await CallAsync("core/obscure", new ObscureParams(clear), RcJson.Default.ObscureParams, RcJson.Default.ObscureResult, cancellationToken)).Obscured;

    public Task<PidResult> PidAsync(CancellationToken cancellationToken) =>
        CallAsync("core/pid", new NoParams(), RcJson.Default.NoParams, RcJson.Default.PidResult, cancellationToken);

    // rcd exits 1.5 s after answering
    public Task QuitAsync(CancellationToken cancellationToken) =>
        CallAsync("core/quit", new NoParams(), RcJson.Default.NoParams, RcJson.Default.NoResult, cancellationToken);

    public void Dispose() => _http.Dispose();

    internal async Task<TResult> CallAsync<TParams, TResult>(
        string method, TParams parameters, JsonTypeInfo<TParams> paramsType, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(method, parameters, paramsType, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Only its message, as rclone sends the call's parameters back with it, which can hold a password
            var error = await response.Content.ReadFromJsonAsync(RcJson.Default.RcError, cancellationToken);
            throw new RcloneException($"rclone's {method} failed: {error?.Error ?? response.ReasonPhrase}");
        }

        return await response.Content.ReadFromJsonAsync(resultType, cancellationToken)
            ?? throw new RcloneException($"rclone's {method} answered with nothing");
    }

    // HttpClient doesn't wrap the SocketException macOS gives when rcd goes just as it connects
    private async Task<HttpResponseMessage> PostAsync<TParams>(
        string method, TParams parameters, JsonTypeInfo<TParams> paramsType, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.PostAsJsonAsync(method, parameters, paramsType, cancellationToken);
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

internal sealed record UnlockParams(string ConfigPassword);

internal sealed record CreateConfigParams(string Name, string Type, IReadOnlyDictionary<string, string> Parameters, ConfigOpt Opt);

internal sealed record UpdateConfigParams(string Name, IReadOnlyDictionary<string, string> Parameters, ConfigOpt Opt);

internal sealed record ConfigOpt(bool NonInteractive, bool All = false, bool Continue = false, string? State = null, string? Result = null);

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
[JsonSerializable(typeof(UnlockParams))]
[JsonSerializable(typeof(RcError))]
[JsonSerializable(typeof(VersionResult))]
internal sealed partial class RcJson : JsonSerializerContext;
