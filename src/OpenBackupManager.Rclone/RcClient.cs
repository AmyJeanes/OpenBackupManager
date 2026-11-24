using System.Net.Sockets;
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

internal sealed record NoParams;

internal sealed record NoResult;

internal sealed record RcError(string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NoParams))]
[JsonSerializable(typeof(NoResult))]
[JsonSerializable(typeof(PidResult))]
[JsonSerializable(typeof(ListRemotesResult))]
[JsonSerializable(typeof(UnlockParams))]
[JsonSerializable(typeof(RcError))]
[JsonSerializable(typeof(VersionResult))]
internal sealed partial class RcJson : JsonSerializerContext;
