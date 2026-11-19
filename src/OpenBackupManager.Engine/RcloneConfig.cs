using System.Security.Cryptography;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// Each remote's rclone config file, all encrypted with one password kept in the secret store
internal sealed class RcloneConfig(AppPaths paths, Secrets secrets)
{
    private const string PasswordName = "rclone-config";

    public string FileFor(string remoteId) => Path.Combine(Folder, remoteId + ".conf");

    public Task<Secret?> ReadPasswordAsync(CancellationToken cancellationToken) => secrets.ReadAsync(PasswordName, cancellationToken);

    // Never made anew while a config is encrypted, as only the old password can read it
    public async Task<Secret> PasswordForSetupAsync(CancellationToken cancellationToken)
    {
        if (await ReadPasswordAsync(cancellationToken) is { } password)
        {
            return password;
        }

        if (Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.conf").Any(RcloneConfigFile.IsEncrypted))
        {
            throw new EngineException(
                "The password for the remotes' rclone config isn't in the secret store, so they need setting up again, or the store it was in brought back");
        }

        var created = new Secret(RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 40));
        await secrets.SaveAsync(PasswordName, created, cancellationToken);
        return created;
    }

    private string Folder => Path.Combine(paths.State, "rclone");
}
