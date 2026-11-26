using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// What an engine that ended without stopping, such as by crashing, may have left. On macOS, which has no
// KillOnParentExit, its rclones run on, a setup's perhaps holding the port its sign-in listens on. A remote's own is
// stopped as it starts, but not one taken out of config.toml since, nor a setup's or the backend list's
internal static class LeftBehind
{
    public static async Task ClearAsync(AppPaths paths, CancellationToken cancellationToken)
    {
        var run = RunFolder.Path(paths);
        if (Directory.Exists(run))
        {
            var sockets = Directory.EnumerateFiles(run, "rc-*.sock")
                .Concat(Directory.EnumerateFiles(run, "su-*.sock"))
                .Concat(Directory.EnumerateFiles(run, "backends.sock"))
                .ToList();
            foreach (var socket in sockets)
            {
                await Rcd.ClearLeftBehindAsync(socket, cancellationToken);
            }
        }

        // Each remote's config is kept. Anything else beside them is a setup's copy, or rclone's own temporary file or
        // backup from saving one, so it holds the same secrets
        var configs = Path.Combine(paths.State, "rclone");
        if (Directory.Exists(configs))
        {
            foreach (var file in Directory.EnumerateFiles(configs, "*.conf*").Where(file => !file.EndsWith(".conf", StringComparison.Ordinal)))
            {
                File.Delete(file);
            }
        }
    }
}
