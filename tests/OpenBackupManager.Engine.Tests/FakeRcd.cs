using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace OpenBackupManager.Engine.Tests;

// Answers rclone's remote control calls on a socket as a test says, with a status and a JSON body for each call's
// name, such as operations/stat, and keeps the names of the calls made. For failures a real rclone can't be made to give
internal sealed class FakeRcd : IAsyncDisposable
{
    private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Func<string, (int Status, string Body)> _answer;
    private readonly Task _serving;

    public FakeRcd(string socket, Func<string, (int Status, string Body)> answer)
    {
        _answer = answer;
        _listener.Bind(new UnixDomainSocketEndPoint(socket));
        _listener.Listen();
        _serving = ServeAsync();
    }

    public ConcurrentQueue<string> Calls { get; } = new();

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Dispose();
        await _serving;
        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        var connections = new List<Task>();
        try
        {
            while (true)
            {
                connections.Add(AnswerAsync(await _listener.AcceptAsync(_stopping.Token)));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        await Task.WhenAll(connections);
    }

    // Each request in turn, as HttpClient keeps the connection for the next
    private async Task AnswerAsync(Socket connection)
    {
        await using var stream = new NetworkStream(connection, ownsSocket: true);
        try
        {
            while (await LineAsync(stream) is { Length: > 0 } request)
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (await LineAsync(stream) is { Length: > 0 } header)
                {
                    var colon = header.IndexOf(':', StringComparison.Ordinal);
                    headers[header[..colon]] = header[(colon + 1)..].Trim();
                }

                await SkipBodyAsync(stream, headers);
                var name = request.Split(' ')[1].TrimStart('/').Split('?')[0];
                Calls.Enqueue(name);
                var (status, body) = _answer(name);
                var bytes = Encoding.UTF8.GetBytes(body);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status} {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n\r\n"));
                await stream.WriteAsync(bytes);
            }
        }
        catch (IOException)
        {
            // The client went
        }
    }

    // JSON is sent in chunks, and an uploaded file with its length
    private static async Task SkipBodyAsync(Stream stream, Dictionary<string, string> headers)
    {
        if (headers.TryGetValue("Content-Length", out var length))
        {
            await stream.ReadExactlyAsync(new byte[int.Parse(length, CultureInfo.InvariantCulture)]);
        }
        else if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding == "chunked")
        {
            while (int.Parse((await LineAsync(stream))!, NumberStyles.HexNumber, CultureInfo.InvariantCulture) is var size and > 0)
            {
                await stream.ReadExactlyAsync(new byte[size + 2]);
            }

            await LineAsync(stream);
        }
    }

    // Without the line's end, or null once the client has gone
    private static async Task<string?> LineAsync(Stream stream)
    {
        var line = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one) == 1)
        {
            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString([.. line]).TrimEnd('\r');
            }

            line.Add(one[0]);
        }

        return null;
    }
}
