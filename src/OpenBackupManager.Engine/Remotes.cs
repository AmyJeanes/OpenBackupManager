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
internal sealed partial class Remotes(ConfigWatcher config, AppPaths paths, ILogger<Remotes> logger) : IHostedService
{
    private Rcd[] _running = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var started = await Task.WhenAll(config.Config.Remotes.Select(remote => StartAsync(remote.Id, cancellationToken)));
        _running = [.. started.OfType<Rcd>()];
    }

    // All at once, as each takes 1.5 s to quit
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_running.Select(rcd => rcd.DisposeAsync().AsTask()));
        _running = [];
    }

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

    // The engine carries on without it, so the other remotes and obm still work
    private async Task<Rcd?> StartAsync(string id, CancellationToken cancellationToken)
    {
        var options = Options(id);
        try
        {
            foreach (var file in new[] { options.Socket, options.Config, options.LogFile })
            {
                PrivateFiles.CreateFolder(Path.GetDirectoryName(file)!);
            }

            var rcd = await Rcd.StartAsync(options, cancellationToken);
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
}
