using System.Net;

namespace OpenBackupManager.App.Windows.UITests;

// A feed folder served over HTTP, sending the start of each package and then holding it, so a download stays part way
// until this is disposed
public sealed class HeldFeed : IDisposable
{
    private readonly string _folder;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _release = new();
    private readonly List<Task> _answering = [];
    private readonly Task _serving;

    public HeldFeed(string folder)
    {
        _folder = folder;
        Url = $"http://localhost:{Ports.Free()}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _serving = ServeAsync();
    }

    public string Url { get; }

    public void Dispose()
    {
        _release.Cancel();
        // Before the listener closes, as answers that are held abort through it
        Task[] answering;
        lock (_answering)
        {
            answering = [.. _answering];
        }

        Task.WaitAll(answering);
        _listener.Close();
        _serving.Wait();
        _release.Dispose();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync();
                lock (_answering)
                {
                    _answering.Add(AnswerAsync(context));
                }
            }
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
        {
            // Closed
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var file = Path.Combine(_folder, Path.GetFileName(context.Request.Url!.AbsolutePath));
            if (!File.Exists(file))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            var bytes = await File.ReadAllBytesAsync(file);
            response.ContentLength64 = bytes.Length;
            if (Path.GetExtension(file) != ".nupkg")
            {
                await response.OutputStream.WriteAsync(bytes);
                response.Close();
                return;
            }

            await response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length / 5));
            await response.OutputStream.FlushAsync();
            await Task.Delay(Timeout.Infinite, _release.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or HttpListenerException or ObjectDisposedException)
        {
            // Released, or the app stopped downloading
        }

        response.Abort();
    }
}
