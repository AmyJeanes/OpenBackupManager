using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenBackupManager.Testing;

// Answers HTTP as a test says: rclone's remote control calls on a Unix socket, for steps the bundled rclone only takes
// with a network and failures a real one can't be made to give, or pages on a loopback port for rclone itself to reach.
// Each answer is a status and a body, chosen by the request's path without its leading /, or the bodies given, in order
public sealed class FakeHttp : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly Func<string, (int Status, string Body)> _answer;
    private readonly string _contentType;
    private readonly Task _serving;

    private FakeHttp(Socket listener, Func<string, (int Status, string Body)> answer, string contentType)
    {
        _listener = listener;
        _answer = answer;
        _contentType = contentType;
        _listener.Listen();
        _serving = ServeAsync();
    }

    // Where it's listening, on a port
    public string? Url => _listener.LocalEndPoint is IPEndPoint port ? $"http://127.0.0.1:{port.Port}/" : null;

    // Each request's path and body
    public List<(string Path, string Body)> Calls { get; } = [];

    public static FakeHttp OnSocket(string socket, params string[] bodies) => OnSocket(socket, InOrder(bodies));

    // As rclone's remote control answers, in JSON
    public static FakeHttp OnSocket(string socket, Func<string, (int Status, string Body)> answer)
    {
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socket));
        return new FakeHttp(listener, answer, "application/json");
    }

    // As a web server answers, in HTML, which rclone's http backend reads folders from
    public static FakeHttp OnPort(Func<string, (int Status, string Body)> answer)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return new FakeHttp(listener, answer, "text/html");
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        await _serving;
    }

    private static Func<string, (int, string)> InOrder(string[] bodies)
    {
        var queue = new Queue<string>(bodies);
        return _ => (200, queue.Dequeue());
    }

    // Each connection on its own, as HttpClient opens another while one it gave up on is still closing
    private async Task ServeAsync()
    {
        var connections = new List<Task>();
        try
        {
            while (true)
            {
                connections.Add(AnswerAsync(await _listener.AcceptAsync()));
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }

        await Task.WhenAll(connections);
    }

    private async Task AnswerAsync(Socket connection)
    {
        await using var stream = new NetworkStream(connection, ownsSocket: true);
        try
        {
            while (await ReadRequestAsync(stream) is { } request)
            {
                lock (Calls)
                {
                    Calls.Add((request.Path, request.Body));
                }

                var (status, answer) = _answer(request.Path);
                var body = Encoding.UTF8.GetBytes(answer);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status} {status}\r\nContent-Type: {_contentType}\r\nContent-Length: {body.Length}\r\n\r\n"));
                if (!request.Head)
                {
                    await stream.WriteAsync(body);
                }
            }
        }
        catch (IOException)
        {
            // The client went
        }
    }

    // HttpClient sends JSON in chunks, as it doesn't know its length first, and an uploaded file with its length
    private static async Task<(string Path, string Body, bool Head)?> ReadRequestAsync(NetworkStream stream)
    {
        if (await ReadLineAsync(stream) is not { } requestLine)
        {
            return null;
        }

        string? length = null;
        var chunked = false;
        while (await ReadLineAsync(stream) is { Length: > 0 } header)
        {
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = header["Content-Length:".Length..].Trim();
            }
            else if (header.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase))
            {
                chunked = header.Contains("chunked", StringComparison.OrdinalIgnoreCase);
            }
        }

        var body = new MemoryStream();
        if (length is not null)
        {
            var all = new byte[int.Parse(length, CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(all);
            body.Write(all);
        }
        else if (chunked)
        {
            while (int.Parse(await ReadLineAsync(stream) ?? "0", NumberStyles.HexNumber, CultureInfo.InvariantCulture) is var size and > 0)
            {
                var chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk);
                body.Write(chunk);
                await ReadLineAsync(stream);
            }

            await ReadLineAsync(stream);
        }

        var parts = requestLine.Split(' ');
        var path = Uri.UnescapeDataString(parts[1].TrimStart('/').Split('?')[0]);
        return (path, Encoding.UTF8.GetString(body.ToArray()), parts[0] == "HEAD");
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (!line.ToString().EndsWith("\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one) == 0)
            {
                return null;
            }

            line.Append((char)one[0]);
        }

        return line.ToString()[..^2];
    }
}
