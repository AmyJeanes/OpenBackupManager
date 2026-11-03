using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// The setting is read on every use, so a change to config.toml applies straight away. Each use waits while secrets
// are being moved, so none is saved to the store they're leaving after it's been listed, and lost when it's emptied
public sealed class Secrets(IServiceProvider services, ConfigWatcher config, FileStore fileStore) : IDisposable
{
    private const string Setting = "secret-store";

    private readonly SemaphoreSlim _lock = new(1, 1);

    // Null when none is chosen yet, on Linux. Reading config.toml has already refused a store this OS doesn't have
    public SecretStoreKind? Chosen => config.Config.SecretStore is { } name
        ? SecretStoreKinds.Parse(name)!.Value
        : SecretStoreKinds.DefaultOn(CurrentOS.Platform);

    public Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default) =>
        UseAsync(store => store.ReadAsync(name, cancellationToken), cancellationToken);

    public Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default) =>
        UseAsync(async store =>
        {
            await store.SaveAsync(name, secret, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) =>
        UseAsync(store => store.RemoveAsync(name, cancellationToken), cancellationToken);

    public void Dispose() => _lock.Dispose();

    internal SecretsStatus Status() => Chosen is { } kind
        ? new SecretsStatus(kind.Name(), kind == SecretStoreKind.File ? fileStore.Path : null)
        : new SecretsStatus();

    // Each is copied and read back before the setting changes, and only then removed from the old store, so wherever
    // this stops, the setting points to a store holding them all. Until the setting changes, a failure puts the new
    // store back as it was, including anything this home already had there
    internal async Task<MigrateSecretsResult> MigrateAsync(string storeName, CancellationToken cancellationToken)
    {
        if (SecretStoreKinds.Parse(storeName) is not { } to || !SecretStoreKinds.NamesOn(CurrentOS.Platform).Contains(storeName))
        {
            throw new EngineException($"This OS has no store called {storeName}. Choose {string.Join(" or ", SecretStoreKinds.NamesOn(CurrentOS.Platform))}");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (config.EnvVarOverriding(Setting) is { } envVar)
            {
                throw new EngineException(
                    $"{envVar} is set where the engine runs, and it wins over config.toml, so the secrets can't be moved by changing {Setting} there. "
                    + $"Set {Setting} in config.toml to {Chosen?.Name()}, remove {envVar} and restart the engine, then run this again");
            }

            if (Chosen == to)
            {
                return Result(to, 0, []);
            }

            // So a store that can't be used, such as Secret Service with no default keyring, is refused before the setting
            // points to it, even with nothing to move
            try
            {
                await StoreFor(to).ListAsync(cancellationToken);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                throw new EngineException(e.Message, e);
            }

            if (Chosen is not { } from)
            {
                try
                {
                    SetStore(to);
                }
                catch (Exception e) when (e is ConfigException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    throw new EngineException(e.Message, e);
                }

                return Result(null, 0, []);
            }

            var (names, moved) = await CopyAsync(from, to, cancellationToken);
            List<string> left = [];
            foreach (var name in names)
            {
                try
                {
                    // Even if the engine is stopping, as the new store is already in use
                    await StoreFor(from).RemoveAsync(name, CancellationToken.None);
                }
                catch (Exception)
                {
                    left.Add(name);
                }
            }

            return Result(from, moved, left);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Returns every name listed, and how many were copied. Some may not be where they're read from, such as one only
    // in a Secret Service keyring other than the default, which removing them all still takes
    private async Task<(IReadOnlyList<string> Names, int Copied)> CopyAsync(SecretStoreKind from, SecretStoreKind to, CancellationToken cancellationToken)
    {
        var source = StoreFor(from);
        var target = StoreFor(to);
        List<(string Name, Secret? Before)> copied = [];
        try
        {
            var names = await source.ListAsync(cancellationToken);
            foreach (var name in names)
            {
                if (await source.ReadAsync(name, cancellationToken) is not { } secret)
                {
                    continue;
                }

                copied.Add((name, await target.ReadAsync(name, cancellationToken)));
                await target.SaveAsync(name, secret, cancellationToken);
                if ((await target.ReadAsync(name, cancellationToken))?.Reveal() != secret.Reveal())
                {
                    throw new InvalidOperationException($"{Title(to)} gave back something different for {name}");
                }
            }

            SetStore(to);
            return (names, copied.Count);
        }
        catch (Exception e)
        {
            var notPutBack = await PutBackAsync(target, copied);
            if (e is OperationCanceledException)
            {
                throw;
            }

            throw new EngineException(
                $"The secrets are still in {Title(from)}, as moving them failed: {e.Message}"
                + (notPutBack.Count > 0 ? $". {Title(to)} may still have copies of {string.Join(", ", notPutBack)}" : ""), e);
        }
    }

    // Each as it was before it was copied over, or removed if it wasn't there. Returns those that couldn't be
    private static async Task<List<string>> PutBackAsync(ISecretStore target, List<(string Name, Secret? Before)> copied)
    {
        List<string> failed = [];
        foreach (var (name, before) in copied)
        {
            try
            {
                if (before is null)
                {
                    await target.RemoveAsync(name, CancellationToken.None);
                }
                else
                {
                    await target.SaveAsync(name, before, CancellationToken.None);
                }
            }
            catch (Exception)
            {
                failed.Add(name);
            }
        }

        return failed;
    }

    private void SetStore(SecretStoreKind to) => config.Set(null, Setting, to.Name());

    private async Task<T> UseAsync<T>(Func<ISecretStore, Task<T>> use, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await use(Chosen is { } kind
                ? StoreFor(kind)
                : throw new EngineException("No store for secrets has been chosen. Set secret-store in config.toml or OBM_SECRET_STORE"));
        }
        finally
        {
            _lock.Release();
        }
    }

    // Every store the OS has is registered, by the engine or its platform project
    private ISecretStore StoreFor(SecretStoreKind kind) => services.GetRequiredKeyedService<ISecretStore>(kind);

    private string Title(SecretStoreKind kind) => kind == SecretStoreKind.File ? fileStore.Path : kind.Title();

    private MigrateSecretsResult Result(SecretStoreKind? from, int moved, List<string> left) => new(moved, left, fileStore.Path, from?.Name());
}
