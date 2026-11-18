using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// An rclone rcd for each remote in config.toml, as each account has its own sign-in, throttling and limits, and a
// crash only takes its own folders
internal sealed partial class Remotes(ConfigWatcher config, AppPaths paths, TimeProvider time, ILogger<Remotes> logger)
    : IHostedService, IDisposable
{
    // Doubling between tries, so one that keeps failing doesn't spin, and back to the first once one has stayed up
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(1);

    private readonly CancellationTokenSource _stopping = new();
    private Task[] _running = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _running = [.. config.Config.Remotes.Select(remote => RunAsync(remote.Id, _stopping.Token))];
        return Task.CompletedTask;
    }

    // All at once, as each takes 1.5 s to quit
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await Task.WhenAll(_running);
    }

    public void Dispose() => _stopping.Dispose();

    // Its files are named from its ID, which config.toml only allows to be letters, digits and hyphens. The socket's is
    // a hash, as a socket's path can only be about 104 bytes on macOS and Linux, which --home's path needs the room for
    internal RcdOptions Options(string id)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..8];
        return new RcdOptions(
            BundledRclone.FilePath,
            Path.Combine(EngineSocket.Folder(paths), $"rc-{hash}.sock"),
            Path.Combine(paths.State, "rclone", id + ".conf"),
            Path.Combine(paths.Cache, "rclone", id),
            Path.Combine(paths.Logs, $"rclone-{id}.log"),
            $"ISV|AmyJeanes|OpenBackupManager/{AppVersion.Current}");
    }

    // Started again whenever it stops or can't start, until the engine stops
    private async Task RunAsync(string id, CancellationToken stopping)
    {
        var retry = FirstRetry;
        try
        {
            while (true)
            {
                var started = time.GetTimestamp();
                if (await StartAsync(id, stopping) is { } rcd)
                {
                    await using (rcd)
                    {
                        await rcd.Exited.WaitAsync(stopping);
                        LogExited(id, rcd.ExitCode, retry.TotalSeconds);
                    }

                    if (time.GetElapsedTime(started) >= LongestRetry)
                    {
                        retry = FirstRetry;
                    }
                }

                await Task.Delay(retry, time, stopping);
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private async Task<Rcd?> StartAsync(string id, CancellationToken stopping)
    {
        var options = Options(id);
        try
        {
            foreach (var file in new[] { options.Socket, options.Config, options.LogFile })
            {
                PrivateFiles.CreateFolder(Path.GetDirectoryName(file)!);
            }

            var rcd = await Rcd.StartAsync(options, stopping);
            LogStarted(id);
            return rcd;
        }
        catch (Exception e) when (e is RcloneException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            LogCantStart(e, id);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started rclone for remote {Id}")]
    private partial void LogStarted(string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't start rclone for remote {Id}")]
    private partial void LogCantStart(Exception e, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "rclone for remote {Id} stopped with exit code {ExitCode}, restarting in {Seconds} s")]
    private partial void LogExited(string id, int exitCode, double seconds);
}
