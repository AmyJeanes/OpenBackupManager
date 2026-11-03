namespace OpenBackupManager.Core.Platform;

// The OS's store for secrets such as the rclone config password, which config.toml refers to by name
public interface ISecretStore
{
    Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default);

    Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default);

    // False if there was nothing to remove
    Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default);

    // The names of every secret this home has saved here, so they can all be moved or removed
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}
