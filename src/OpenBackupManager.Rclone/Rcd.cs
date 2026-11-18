using System.Diagnostics;

namespace OpenBackupManager.Rclone;

/// <summary>
/// An rclone rcd, which the engine runs one of for each remote, reached through rc on a Unix socket in a folder only
/// the user can open, so it needs no password
/// </summary>
public sealed class Rcd : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

    // core/quit takes 1.5 s, and about 3 with a job running. Windows gives the engine 5 at sign-out, and killing it
    // loses nothing
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(4);

    private readonly Process _process;

    private Rcd(Process process, RcClient client)
    {
        _process = process;
        Client = client;
        Exited = process.WaitForExitAsync();
    }

    public RcClient Client { get; }

    // However it ends, stopped or not
    public Task Exited { get; }

    public int ExitCode => _process.ExitCode;

    public static async Task<Rcd> StartAsync(RcdOptions options, CancellationToken cancellationToken)
    {
        // Only this home's engine starts one here, so one answering was left by an engine that ended without stopping
        // it, as on macOS, which has no KillOnParentExit. A socket left by one that was killed stops the next binding it
        await StopLeftBehindAsync(options.Socket, cancellationToken);
        File.Delete(options.Socket);

        using var none = File.OpenNullHandle();
        var start = new ProcessStartInfo(options.Rclone)
        {
            // Its own config and cache, as rclone's defaults are the user's own rclone's
            ArgumentList =
            {
                "rcd", "--rc-addr", "unix://" + options.Socket, "--rc-no-auth", "--config", options.Config,
                "--cache-dir", options.CacheFolder, "--log-file", options.LogFile, "--user-agent", options.UserAgent,
                // As much as the engine keeps of its own
                "--log-file-max-size", "10M", "--log-file-max-backups", "7",
            },
            // None of the engine's, not even its console, as rclone writes everything to its log file, its crashes too
            StandardInputHandle = none,
            StandardOutputHandle = none,
            StandardErrorHandle = none,
            InheritedHandles = [],
        };

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            start.KillOnParentExit = true;
        }

        var process = Process.Start(start) ?? throw new RcloneException($"{options.Rclone} didn't start");
        var client = new RcClient(options.Socket);
        try
        {
            await WaitUntilAnsweringAsync(process, client, options.LogFile, cancellationToken);
            return new Rcd(process, client);
        }
        catch
        {
            client.Dispose();
            process.Kill();
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Once it's gone, another may have its socket
            if (!_process.HasExited)
            {
                await StopAsync(_process, Client);
            }
        }
        finally
        {
            Client.Dispose();
            _process.Dispose();
        }
    }

    // Killed if it won't quit, which leaves its socket for the next start to delete
    private static async Task StopAsync(Process process, RcClient client)
    {
        using var timeout = new CancellationTokenSource(QuitTimeout);
        try
        {
            await client.QuitAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception e) when (e is HttpRequestException or RcloneException or OperationCanceledException)
        {
        }

        if (!process.HasExited)
        {
            process.Kill();
            await process.WaitForExitAsync();
        }
    }

    // Nothing answers after a crash, and the connection is refused at once
    private static async Task StopLeftBehindAsync(string socket, CancellationToken cancellationToken)
    {
        using var client = new RcClient(socket);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QuitTimeout);
        int pid;
        try
        {
            pid = (await client.PidAsync(timeout.Token)).Pid;
        }
        catch (Exception e) when (e is HttpRequestException or RcloneException
            || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return;
        }

        if (Process.TryGetProcessById(pid, out var process))
        {
            using (process)
            {
                await StopAsync(process, client);
            }
        }
    }

    // The socket only appears once it's listening
    private static async Task WaitUntilAnsweringAsync(Process process, RcClient client, string logFile, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);
        try
        {
            while (true)
            {
                if (process.HasExited)
                {
                    throw new RcloneException($"rclone rcd stopped as it started, with exit code {process.ExitCode}. Its log is {logFile}");
                }

                try
                {
                    await client.VersionAsync(timeout.Token);
                    return;
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(50, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RcloneException($"rclone rcd didn't answer within {StartTimeout.TotalSeconds} seconds. Its log is {logFile}");
        }
    }
}

public sealed record RcdOptions(string Rclone, string Socket, string Config, string CacheFolder, string LogFile, string UserAgent);
