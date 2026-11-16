using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Daemon;

// obmd --remove-secrets, which obm data remove runs once the engine has stopped, as only obmd has the OS's stores.
// From each of them, as moving them can leave some behind. Not secrets.json, which goes with the rest of the data, even
// if it's too broken to read. It exits with 1 if a store may still hold some, so obm data remove keeps the rest, with
// the settings saying where they are, for when it's run again
internal static class SecretsRemoval
{
    public static async Task<int> RunAsync(AppPaths paths, Action<IServiceCollection> platform, CancellationToken cancellationToken)
    {
        // So no engine saves one while they're being removed
        ProcessLock? taken;
        try
        {
            taken = ProcessLock.TryTake("engine", paths);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"obmd: the engine's lock in {paths.State} can't be taken, so its secrets can't be removed. {e.Message}", cancellationToken);
            return 1;
        }

        using var engineLock = taken;
        if (engineLock is null)
        {
            await Console.Error.WriteLineAsync("obmd: the engine is running, so its secrets can't be removed", cancellationToken);
            return 1;
        }

        var services = new ServiceCollection();
        platform(services);
        using var provider = services.BuildServiceProvider();
        var chosen = Chosen(paths);
        var left = false;
        foreach (var kind in SecretStoreKinds.NamesOn(CurrentOS.Platform).Select(name => SecretStoreKinds.Parse(name)!.Value).Where(kind => kind != SecretStoreKind.File))
        {
            var title = kind.Title();
            try
            {
                var store = provider.GetRequiredKeyedService<ISecretStore>(kind);
                var names = await store.ListAsync(cancellationToken);
                foreach (var name in names)
                {
                    await store.RemoveAsync(name, cancellationToken);
                }

                if (names.Count > 0)
                {
                    Console.WriteLine($"Removed {names.Count} {(names.Count == 1 ? "secret" : "secrets")} from {title}");
                }
            }
            // Such as Secret Service on a server, where nothing could have been saved, unless it's the store chosen
            catch (SecretStoreUnavailableException e)
            {
                if (kind == chosen)
                {
                    await Console.Error.WriteLineAsync($"obmd: secrets are kept in {title}, which can't be reached, so any kept there are still in it. {e.Message}", cancellationToken);
                    left = true;
                }
            }
            catch (SecretStoreLockedException)
            {
                await Console.Error.WriteLineAsync($"obmd: {title} is locked, so any secrets kept there are still in it. Unlock it and run obm data remove again to remove them", cancellationToken);
                left = true;
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync($"obmd: couldn't look in {title}, so any secrets kept there may still be in it: {e.Message}", cancellationToken);
                left = true;
            }
        }

        return left ? 1 : 0;
    }

    // As config.toml says, or this OS's own store when it says none. Null if it can't be read, as it may be what's wrong
    private static SecretStoreKind? Chosen(AppPaths paths)
    {
        try
        {
            return new ConfigFile(paths.ConfigFile).Read().Config is not { } config ? null
                : config.SecretStore is { } name ? SecretStoreKinds.Parse(name)
                : SecretStoreKinds.DefaultOn(CurrentOS.Platform);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
