using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// What an engine that ended without stopping, such as by crashing, may have left. On macOS, which has no
// KillOnParentExit, its rclones run on, a setup's perhaps holding the port its sign-in listens on. A remote's own is
// stopped as it starts, but not one taken out of config.toml since, nor a setup's or the backend list's. What can't be
// cleared, such as a file antivirus has open, is left for the next start
internal static partial class LeftBehind
{
    public static async Task ClearAsync(AppPaths paths, ILogger logger, CancellationToken cancellationToken)
    {
        // First, as another user's link could lead anywhere, and a socket there name a process of this user's to stop
        RunFolder.Create(paths);
        var run = RunFolder.Path(paths);
        var sockets = Directory.EnumerateFiles(run, "rc-*.sock")
            .Concat(Directory.EnumerateFiles(run, "su-*.sock"))
            .Concat(Directory.EnumerateFiles(run, "backends.sock"))
            .ToList();
        foreach (var socket in sockets)
        {
            try
            {
                await Rcd.ClearLeftBehindAsync(socket, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogNotCleared(logger, socket, e);
            }
        }

        // Each remote's config is kept. Anything else beside them is a setup's copy, or rclone's own temporary file or
        // backup from saving one, so it holds the same secrets
        if (RcloneConfig.ExistingFolder(paths) is { } configs)
        {
            foreach (var file in Directory.EnumerateFiles(configs, "*.conf*").Where(file => !file.EndsWith(".conf", StringComparison.Ordinal)))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    LogNotCleared(logger, file, e);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't clear {Path}, left by an engine that ended without stopping")]
    private static partial void LogNotCleared(ILogger logger, string path, Exception exception);
}
