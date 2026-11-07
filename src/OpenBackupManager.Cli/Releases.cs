using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBackupManager.Core;
using Velopack;

namespace OpenBackupManager.Cli;

// The releases on GitHub, from its API, or from OBM_UPDATE_SOURCE in its place, for trying updates locally
internal sealed class Releases : IDisposable
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private static readonly string Repository = new Uri(typeof(Releases).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "RepositoryUrl").Value!).AbsolutePath.Trim('/');

    // No timeout of its own, as a download can take minutes on a slow connection
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _api;

    public Releases(string? source)
    {
        _api = (source ?? "https://api.github.com").TrimEnd('/');
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenBackupManager", AppVersion.Current.Split('+')[0]));
    }

    // The highest release, and a prerelease only if they're wanted. Null if there are none
    public async Task<Release?> NewestAsync(bool prereleases, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_api}/repos/{Repository}/releases?per_page=30");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        List<GitHubRelease> releases;
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                && response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Single() == "0")
            {
                throw new UpdateException("GitHub is limiting how often this computer can check for updates. Try again within the hour");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            releases = await JsonSerializer.DeserializeAsync(stream, GitHubJson.Default.ListGitHubRelease, timeout.Token) ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException
            || e is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("couldn't reach GitHub to check for updates. " + (e is OperationCanceledException ? "It didn't answer within 30 seconds" : e.Message), e);
        }

        return releases
            .Where(r => !r.Draft)
            .Select(r => SemanticVersion.TryParse(r.TagName.TrimStart('v'), out var version) ? new Release(version, r.Assets) : null)
            .OfType<Release>()
            .Where(r => prereleases || !r.Version.IsPrerelease)
            .MaxBy(r => r.Version);
    }

    // Into a file, with its SHA-256 worked out as it comes
    public async Task<byte[]> DownloadAsync(GitHubAsset asset, string file, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var from = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var to = File.Create(file);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await from.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return hash.GetHashAndReset();
        }
        catch (HttpRequestException e)
        {
            throw new UpdateException($"couldn't download {asset.Name}, so nothing was changed. {e.Message}", e);
        }
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record Release(SemanticVersion Version, IReadOnlyList<GitHubAsset> Assets);

internal sealed record GitHubRelease(string TagName, bool Draft, IReadOnlyList<GitHubAsset> Assets);

// Digest is sha256: then the hash in hex, which GitHub works out for every file uploaded to a release
internal sealed record GitHubAsset(string Name, string BrowserDownloadUrl, string? Digest);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(List<GitHubRelease>))]
internal sealed partial class GitHubJson : JsonSerializerContext;

internal sealed class UpdateException : Exception
{
    public UpdateException(string message)
        : base(message)
    {
    }

    public UpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
