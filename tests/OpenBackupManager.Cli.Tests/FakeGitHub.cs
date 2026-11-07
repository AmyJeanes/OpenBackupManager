using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenBackupManager.Cli.Tests;

// Answers as GitHub's releases API does, with its downloads, for obm update through OBM_UPDATE_SOURCE
internal sealed class FakeGitHub : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly JsonArray _releases = [];
    private readonly Dictionary<string, byte[]> _downloads = [];

    public FakeGitHub()
    {
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(AnswerAsync);
    }

    public string Url { get; }

    // As release downloads are named
    public static string Runtime { get; } =
        (OperatingSystem.IsMacOS() ? "osx-" : "linux-") + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    // As GitHub answers once an address has made too many requests
    public bool RateLimited { get; set; }

    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string> { ["OBM_UPDATE_SOURCE"] = Url };

    // Digest is sha256: then the hash in hex, as GitHub gives, unless the test says otherwise
    public void Add(string version, params Asset[] assets)
    {
        var list = new JsonArray();
        foreach (var asset in assets)
        {
            _downloads[asset.Name] = asset.Content;
            list.Add(new JsonObject
            {
                ["name"] = asset.Name,
                ["browser_download_url"] = $"{Url}/download/{asset.Name}",
                ["digest"] = asset.Digest ?? (asset.HasDigest ? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(asset.Content)) : null),
            });
        }

        _releases.Add(new JsonObject
        {
            ["tag_name"] = "v" + version,
            ["draft"] = false,
            ["prerelease"] = version.Contains('-', StringComparison.Ordinal),
            ["assets"] = list,
        });
    }

    public void Dispose() => _listener.Close();

    // obm and obmd as they are, as they're what runs, but dated later as a newer build's would be, with licences
    // to tell it apart
    [UnsupportedOSPlatform("windows")]
    public static byte[] NewVersionOf(string programs)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar))
        {
            foreach (var name in (string[])["obm", "obmd"])
            {
                var path = Path.Combine(programs, name);
                using var content = File.OpenRead(path);
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    Mode = File.GetUnixFileMode(path),
                    ModificationTime = File.GetLastWriteTimeUtc(path).AddMinutes(1),
                    DataStream = content,
                });
            }

            foreach (var name in (string[])["LICENSE", "THIRD-PARTY-NOTICES.txt"])
            {
                using var text = new MemoryStream(Encoding.UTF8.GetBytes($"The new version's {name}"));
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { Mode = (UnixFileMode)0b110_100_100, DataStream = text });
            }
        }

        return stream.ToArray();
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private async Task AnswerAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            using var response = context.Response;
            var path = context.Request.Url!.AbsolutePath;
            byte[] body;
            if (RateLimited)
            {
                response.StatusCode = 403;
                response.Headers["x-ratelimit-remaining"] = "0";
                body = Encoding.UTF8.GetBytes("""{"message":"API rate limit exceeded"}""");
            }
            else if (path == "/repos/AmyJeanes/OpenBackupManager/releases")
            {
                body = Encoding.UTF8.GetBytes(_releases.ToJsonString());
            }
            else if (path.StartsWith("/download/", StringComparison.Ordinal) && _downloads.TryGetValue(path["/download/".Length..], out var download))
            {
                body = download;
            }
            else
            {
                response.StatusCode = 404;
                body = [];
            }

            await response.OutputStream.WriteAsync(body);
        }
    }
}

internal sealed record Asset(string Name, byte[] Content)
{
    public bool HasDigest { get; init; } = true;

    public string? Digest { get; init; }
}
