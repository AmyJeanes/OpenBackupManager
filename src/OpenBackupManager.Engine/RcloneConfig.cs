using System.Security.Cryptography;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// Each remote's rclone config file, all encrypted with one password kept in the secret store
internal sealed class RcloneConfig(AppPaths paths, Secrets secrets, ConfigWatcher config) : IDisposable
{
    // So two setups at once can't each make one, leaving a config encrypted with a password that's been replaced
    private readonly SemaphoreSlim _setup = new(1, 1);

    public string FileFor(string remoteId) => Path.Combine(Folder, remoteId + ".conf");

    public Task<Secret?> ReadPasswordAsync(CancellationToken cancellationToken) => secrets.ReadAsync(SecretName.RcloneConfig, cancellationToken);

    // Where the password is kept, as secret-store names it
    public string? Store => secrets.Chosen?.Name();

    // A new one leaves every other encrypted config unreadable, so they're deleted, only when asked, as bringing the
    // old store back would keep them. Not the remote being set up's own, which finishing replaces and cancelling keeps.
    // One taken out of config.toml isn't asked about, as it's no loss
    public async Task<Secret> PasswordForSetupAsync(string remoteId, bool startAfresh, CancellationToken cancellationToken)
    {
        await _setup.WaitAsync(cancellationToken);
        try
        {
            if (await ReadPasswordAsync(cancellationToken) is { } password)
            {
                return password;
            }

            if (ExistingFolder(paths) is { } folder)
            {
                var others = Directory.EnumerateFiles(folder, "*.conf").Where(file => file != FileFor(remoteId) && RcloneConfigFile.IsEncrypted(file)).ToList();
                var kept = others.Select(Path.GetFileNameWithoutExtension).Where(id => config.Config.Remotes.Any(remote => remote.Id == id)).ToList();
                if (kept.Count > 0 && !startAfresh)
                {
                    var names = kept.Order(StringComparer.Ordinal);
                    throw new EngineException(
                        EngineProblem.NoConfigPassword,
                        $"The password for the rclone config isn't in the secret store, so {string.Join(", ", names)} can't be read. Bring back "
                        + $"the store it was in, or start afresh, setting them all up again. See {AppLinks.Guide("files/#lost-config-password")}");
                }

                foreach (var file in others)
                {
                    File.Delete(file);
                }
            }

            var created = new Secret(RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 40));
            await secrets.SaveAsync(SecretName.RcloneConfig, created, cancellationToken);
            return created;
        }
        finally
        {
            _setup.Release();
        }
    }

    public void Dispose() => _setup.Dispose();

    // Null if it isn't there. Not a link, as another user's could lead starting afresh, or clearing what a crash left,
    // to delete what's there
    public static string? ExistingFolder(AppPaths paths)
    {
        var folder = FolderIn(paths);
        var info = new DirectoryInfo(folder);
        if (!info.Exists)
        {
            return null;
        }

        return info.LinkTarget is null ? folder : throw new EngineException($"{folder} is a link, so the engine won't keep remotes' configs there");
    }

    private static string FolderIn(AppPaths paths) => Path.Combine(paths.State, "rclone");

    private string Folder => FolderIn(paths);
}
