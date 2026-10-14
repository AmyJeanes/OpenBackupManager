namespace OpenBackupManager.Core.Platform;

// The OS's store for secrets such as the rclone config password, which config.toml refers to by name
public interface ISecretStore
{
    Secret? Read(string name);

    void Save(string name, Secret secret);

    // False if there was nothing to remove
    bool Remove(string name);
}
