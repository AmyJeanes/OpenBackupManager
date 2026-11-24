using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm remote: the remotes in config.toml, each with its own rclone
internal static class Remote
{
    public static async Task<int> RetryAsync(AppPaths paths, string id, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
            if (engine is null)
            {
                await error.WriteLineAsync("obm: the engine isn't running. It tries every remote as it starts", cancellationToken);
                return 1;
            }

            await engine.RetryRemoteAsync(id, cancellationToken);
            output.WriteLine($"Trying {id} again. obm status shows how it goes");
            return 0;
        }
        catch (EngineException e)
        {
            await error.WriteLineAsync("obm: " + e.Message, cancellationToken);
            return 1;
        }
    }
}
