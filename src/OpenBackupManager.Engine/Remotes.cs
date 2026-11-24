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
internal sealed partial class Remotes(ConfigWatcher config, RcloneConfig rcloneConfig, AppPaths paths, TimeProvider time, ILogger<Remotes> logger)
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

    // Ends once every remote has stopped trying
    internal Task Running => Task.WhenAll(_running);

    // Its files are named from its ID, which config.toml only allows to be letters, digits and hyphens. The socket's is
    // a hash, as the run folder limits how long its names can be
    internal RcdOptions Options(string id)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..8];
        return new RcdOptions(
            BundledRclone.FilePath,
            RunFolder.Socket(paths, $"rc-{hash}.sock"),
            rcloneConfig.FileFor(id),
            Path.Combine(paths.Cache, "rclone", id),
            Path.Combine(paths.Logs, $"rclone-{id}.log"),
            $"ISV|AmyJeanes|OpenBackupManager/{AppVersion.Current}");
    }

    // Until the engine stops, or it needs setting up
    private async Task RunAsync(string id, CancellationToken stopping)
    {
        var retry = FirstRetry;
        try
        {
            while (true)
            {
                var started = time.GetTimestamp();
                var (rcd, tryAgain) = await StartAsync(id, stopping);
                if (!tryAgain)
                {
                    return;
                }

                if (rcd is not null)
                {
                    await using (rcd)
                    {
                        await rcd.Exited.WaitAsync(stopping);
                        if (time.GetElapsedTime(started) >= LongestRetry)
                        {
                            retry = FirstRetry;
                        }

                        LogExited(id, rcd.ExitCode, retry.TotalSeconds);
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

    // Not retried when it needs setting up. Its file is left, as putting its password back brings it back when the
    // engine next starts
    private async Task<(Rcd? Rcd, bool TryAgain)> StartAsync(string id, CancellationToken stopping)
    {
        var options = Options(id);
        if (!File.Exists(options.Config))
        {
            LogNeedsSettingUp(id, "it has no rclone config");
            return (null, false);
        }

        try
        {
            string? password = null;
            if (RcloneConfigFile.IsEncrypted(options.Config))
            {
                if (await rcloneConfig.ReadPasswordAsync(stopping) is not { } secret)
                {
                    LogNeedsSettingUp(id, "the password for its rclone config isn't in the secret store");
                    return (null, false);
                }

                password = secret.Reveal();
            }

            RunFolder.Create(paths);
            PrivateFiles.CreateFolder(Path.GetDirectoryName(options.LogFile)!);

            var rcd = await Rcd.StartAsync(options, password, stopping);
            // rclone reads its config lazily, so a wrong password or a damaged file only shows here
            try
            {
                await rcd.Client.ListRemotesAsync(stopping);
            }
            // Not rclone's message, which can quote a line of the config
            catch (RcloneException)
            {
                await rcd.DisposeAsync();
                LogNeedsSettingUp(id, (password is null
                    ? "rclone can't read its config"
                    : "its rclone config can't be read with the password in the secret store") + $". {options.LogFile} says why");
                return (null, false);
            }
            catch
            {
                await rcd.DisposeAsync();
                throw;
            }

            LogStarted(id);
            return (rcd, true);
        }
        // Such as a locked secret store
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException or UnauthorizedAccessException
            or Win32Exception or EngineException or InvalidOperationException or InvalidDataException)
        {
            LogCantStart(e, id);
            return (null, true);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started rclone for remote {Id}")]
    private partial void LogStarted(string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't start rclone for remote {Id}")]
    private partial void LogCantStart(Exception e, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote {Id} needs setting up, as {Reason}")]
    private partial void LogNeedsSettingUp(string id, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "rclone for remote {Id} stopped with exit code {ExitCode}, restarting in {Seconds} s")]
    private partial void LogExited(string id, int exitCode, double seconds);
}
