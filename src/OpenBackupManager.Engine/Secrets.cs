using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// The setting is read on every use, so a change to config.toml applies straight away
public sealed class Secrets(IServiceProvider services, ConfigWatcher config, FileStore fileStore)
{
    // Null when none is chosen yet, on Linux. Reading config.toml has already refused a store this OS doesn't have
    public SecretStoreKind? Chosen => config.Config.SecretStore is { } name
        ? SecretStoreKinds.Parse(name)!.Value
        : SecretStoreKinds.DefaultOn(CurrentOS.Platform);

    // Every store the OS has is registered, by the engine or its platform project
    public ISecretStore Store => Chosen is { } kind
        ? services.GetRequiredKeyedService<ISecretStore>(kind)
        : throw new EngineException("No store for secrets has been chosen. Set secret-store in config.toml or OBM_SECRET_STORE");

    internal SecretsStatus Status() => Chosen is { } kind
        ? new SecretsStatus(kind.Name(), kind == SecretStoreKind.File ? fileStore.Path : null)
        : new SecretsStatus();
}
