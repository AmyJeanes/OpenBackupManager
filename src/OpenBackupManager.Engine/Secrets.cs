using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// The setting is read on every use, so a change to config.toml applies straight away. Each use waits while secrets
// are being moved, so none is saved to the store they're leaving after it's been listed, and lost when it's emptied
public sealed partial class Secrets(IServiceProvider services, ConfigWatcher config, FileStore fileStore, ILogger<Secrets> logger) : IDisposable
{
    private const string Setting = "secret-store";

    private readonly SemaphoreSlim _lock = new(1, 1);

    // Those given by the environment whose stored copy has been looked for
    private readonly ConcurrentDictionary<string, bool> _tidied = new();

    // Null when none is chosen yet, on Linux. Reading config.toml has already refused a store this OS doesn't have
    public SecretStoreKind? Chosen => config.Config.SecretStore is { } name
        ? SecretStoreKinds.Parse(name)!.Value
        : SecretStoreKinds.DefaultOn(CurrentOS.Platform);

    // From its environment variable before the store, so a container can keep it out of the volume, such as in a
    // Docker secret, and a copy of the volume doesn't hold both a config and the password that opens it
    public async Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        if (FromEnvironment(name) is not { } given)
        {
            return await UseAsync(store => store.ReadAsync(name, cancellationToken), cancellationToken);
        }

        await RemoveStoredCopyAsync(name, given, cancellationToken);
        return given;
    }

    public Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default) =>
        UseAsync(async store =>
        {
            await store.SaveAsync(name, secret, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) =>
        UseAsync(store => store.RemoveAsync(name, cancellationToken), cancellationToken);

    public void Dispose() => _lock.Dispose();

    internal SecretsStatus Status()
    {
        var fromEnvironment = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(variable => EnvVarPattern().IsMatch(variable) && Environment.GetEnvironmentVariable(variable) is { Length: > 0 })
            .Order(StringComparer.Ordinal)
            .ToList() is { Count: > 0 } found ? found : null;
        return Chosen is { } kind
            ? new SecretsStatus(kind.Name(), kind == SecretStoreKind.File ? fileStore.Path : null, fromEnvironment)
            : new SecretsStatus(FromEnvironment: fromEnvironment);
    }

    // Such as one setup made before it was given, as the point of giving it is that it's not kept here. Only the same
    // value, as a different one is the one that works. Once each run, or again after a failed look
    private async Task RemoveStoredCopyAsync(string name, Secret given, CancellationToken cancellationToken)
    {
        if (!_tidied.TryAdd(name, true))
        {
            return;
        }

        try
        {
            if (await UseAsync(async store => (await store.ReadAsync(name, cancellationToken))?.Reveal() == given.Reveal() && await store.RemoveAsync(name, cancellationToken), cancellationToken))
            {
                LogRemovedStoredCopy(name);
            }
        }
        // Such as a locked store, or none chosen. It's still given, so this can wait
        catch (Exception e) when (e is EngineException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _tidied.TryRemove(name, out _);
        }
    }

    private static Secret? FromEnvironment(string name)
    {
        var variable = SecretName.EnvVar(name);
        var value = Environment.GetEnvironmentVariable(variable);
        var file = Environment.GetEnvironmentVariable(variable + "_FILE");
        if (value is { Length: > 0 } && file is { Length: > 0 })
        {
            throw new EngineException($"Both {variable} and {variable}_FILE are set, so it's not clear which to use. Set only one");
        }

        if (value is { Length: > 0 })
        {
            return new Secret(value);
        }

        if (file is not { Length: > 0 })
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new EngineException($"{variable}_FILE is {file}, which can't be read: {e.Message}", e);
        }

        // As an editor or echo adds them, and as Docker images' own _FILE variables do. Not spaces, which may be in it
        text = text.TrimEnd('\r', '\n');
        return text.Length > 0 ? new Secret(text) : throw new EngineException($"{file}, which {variable}_FILE names, is empty");
    }

    [GeneratedRegex(@"\AOBM_[A-Z0-9_]+_SECRET(_FILE)?\z")]
    private static partial Regex EnvVarPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Name} from the secret store, as its environment variable gives it")]
    private partial void LogRemovedStoredCopy(string name);

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

            // So an unusable store, such as Secret Service with no default keyring, is refused before the setting
            // points to it, even with nothing to move
            try
            {
                await StoreFor(to).ListAsync(cancellationToken);
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
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

            var copied = await CopyAsync(from, to, cancellationToken);
            List<string> left = [];
            foreach (var name in copied)
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

            return Result(from, copied.Count, left);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Returns the names copied and read back, so only they are removed from the old store. One listed that can't be
    // read, such as from a keyring that's locked since, stops it, as the new store would be used without it
    private async Task<IReadOnlyList<string>> CopyAsync(SecretStoreKind from, SecretStoreKind to, CancellationToken cancellationToken)
    {
        var source = StoreFor(from);
        var target = StoreFor(to);
        List<(string Name, Secret? Before)> copied = [];
        try
        {
            foreach (var name in await source.ListAsync(cancellationToken))
            {
                if (await source.ReadAsync(name, cancellationToken) is not { } secret)
                {
                    throw new InvalidOperationException($"it listed {name} but gave nothing back for it");
                }

                copied.Add((name, await target.ReadAsync(name, cancellationToken)));
                await target.SaveAsync(name, secret, cancellationToken);
                if ((await target.ReadAsync(name, cancellationToken))?.Reveal() != secret.Reveal())
                {
                    throw new InvalidOperationException($"{Title(to)} gave back something different for {name}");
                }
            }

            SetStore(to);
            return [.. copied.Select(c => c.Name)];
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
                : throw new EngineException("No store for secrets has been chosen. Set secret-store in config.toml"));
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
