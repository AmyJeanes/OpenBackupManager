using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace OpenBackupManager.Rclone.Tests;

// Answers rc calls on a Unix socket with the bodies given, in order, for steps the bundled rclone only takes with a
// network, such as a backend's error once signed in
internal sealed class FakeRc : IAsyncDisposable
{
    private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly Queue<string> _answers;
    private readonly Task _serving;

    public FakeRc(string socket, params string[] answers)
    {
        _answers = new Queue<string>(answers);
        _listener.Bind(new UnixDomainSocketEndPoint(socket));
        _listener.Listen();
        _serving = ServeAsync();
    }

    // Each call's method and body
    public List<(string Method, string Body)> Calls { get; } = [];

    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        try
        {
            await _serving;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ServeAsync()
    {
        while (true)
        {
            using var connection = await _listener.AcceptAsync();
            await using var stream = new NetworkStream(connection);
            while (await ReadRequestAsync(stream) is { } request)
            {
                Calls.Add(request);
                var body = Encoding.UTF8.GetBytes(_answers.Dequeue());
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n"));
                await stream.WriteAsync(body);
            }
        }
    }

    // HttpClient sends JSON in chunks, as it doesn't know its length first
    private static async Task<(string, string)?> ReadRequestAsync(NetworkStream stream)
    {
        if (await ReadLineAsync(stream) is not { } requestLine)
        {
            return null;
        }

        while (await ReadLineAsync(stream) is { Length: > 0 })
        {
        }

        var body = new MemoryStream();
        while (int.Parse(await ReadLineAsync(stream) ?? "0", NumberStyles.HexNumber, CultureInfo.InvariantCulture) is var size and > 0)
        {
            var chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk);
            body.Write(chunk);
            await ReadLineAsync(stream);
        }

        await ReadLineAsync(stream);
        return (requestLine.Split(' ')[1].TrimStart('/'), Encoding.UTF8.GetString(body.ToArray()));
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
