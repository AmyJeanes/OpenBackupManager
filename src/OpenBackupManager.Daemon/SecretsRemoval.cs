using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Daemon;

// obmd --remove-secrets, which obm data remove runs once the engine has stopped, as only obmd has the OS's stores.
// From every store, as moving them can leave some behind, and without the settings, which may be what's wrong
internal static class SecretsRemoval
{
    public static async Task<int> RunAsync(AppPaths paths, Action<IServiceCollection> platform, CancellationToken cancellationToken)
    {
        // So no engine saves one while they're being removed
        using var engineLock = ProcessLock.TryTake("engine", paths);
        if (engineLock is null)
        {
            await Console.Error.WriteLineAsync("obmd: the engine is running, so its secrets can't be removed");
            return 1;
        }

        var services = new ServiceCollection();
        var file = new FileStore(Path.Combine(paths.State, "secrets.json"));
        services.AddKeyedSingleton<ISecretStore>(SecretStoreKind.File, file);
        platform(services);
        using var provider = services.BuildServiceProvider();
        foreach (var kind in SecretStoreKinds.NamesOn(CurrentOS.Platform).Select(name => SecretStoreKinds.Parse(name)!.Value))
        {
            var title = kind == SecretStoreKind.File ? file.Path : kind.Title();
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
            // Such as Secret Service on a server, which may never have been used, so the rest are still removed
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync($"obmd: couldn't look in {title}, so any secrets there are left: {e.Message}");
            }
        }

        return 0;
    }
}
