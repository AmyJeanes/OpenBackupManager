using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace OpenBackupManager.Testing;

// Answers rclone's remote control calls on a Unix socket, for steps the bundled rclone only takes with a network, such
// as a backend's error once signed in, and failures a real one can't be made to give when a test needs them. Each
// answer is a status and a body, chosen by the call's method, or the bodies given, in order
public sealed class FakeRc : IAsyncDisposable
{
    private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly Func<string, (int Status, string Body)> _answer;
    private readonly Task _serving;

    public FakeRc(string socket, params string[] answers)
        : this(socket, InOrder(answers))
    {
    }

    public FakeRc(string socket, Func<string, (int Status, string Body)> answer)
    {
        _answer = answer;
        _listener.Bind(new UnixDomainSocketEndPoint(socket));
        _listener.Listen();
        _serving = ServeAsync();
    }

    // Each call's method and body
    public List<(string Method, string Body)> Calls { get; } = [];

    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        await _serving;
    }

    private static Func<string, (int, string)> InOrder(string[] answers)
    {
        var queue = new Queue<string>(answers);
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
                    Calls.Add(request);
                }

                var (status, answer) = _answer(request.Method);
                var body = Encoding.UTF8.GetBytes(answer);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status} {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"));
                await stream.WriteAsync(body);
            }
        }
        catch (IOException)
        {
            // The client went
        }
    }

    // HttpClient sends JSON in chunks, as it doesn't know its length first, and an uploaded file with its length
    private static async Task<(string Method, string Body)?> ReadRequestAsync(NetworkStream stream)
    {
        if (await ReadLineAsync(stream) is not { } requestLine)
        {
            return null;
        }

        string? length = null;
        while (await ReadLineAsync(stream) is { Length: > 0 } header)
        {
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = header["Content-Length:".Length..].Trim();
            }
        }

        var body = new MemoryStream();
        if (length is not null)
        {
            var all = new byte[int.Parse(length, CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(all);
            body.Write(all);
        }
        else
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

        return (requestLine.Split(' ')[1].TrimStart('/').Split('?')[0], Encoding.UTF8.GetString(body.ToArray()));
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
