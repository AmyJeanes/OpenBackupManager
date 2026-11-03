using System.Globalization;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm secrets migrate: moves this home's secrets to another store
internal static class Migrate
{
    // Long enough for an engine that's just starting
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(AppPaths paths, string store, CancellationToken cancellationToken)
    {
        try
        {
            using var engine = await EngineClient.ConnectAsync(paths, Timeout, cancellationToken);
            if (engine is null)
            {
                await Console.Error.WriteLineAsync("obm: the engine isn't running. Start it, then run this again");
                return 1;
            }

            var result = await engine.MigrateSecretsAsync(store, cancellationToken);
            Console.WriteLine(Moved(result, store));
            if (store == SecretStoreKind.SecretService.Name() && OperatingSystem.IsLinux() && SystemdService.Lingering)
            {
                Console.WriteLine(SystemdService.LockedWhileSignedOut);
            }

            if (result.Left.Count > 0)
            {
                await Console.Error.WriteLineAsync(
                    $"obm: {Where(result.From!, result)} still has {string.Join(", ", result.Left)}, which couldn't be removed from it. Remove them there by hand");
                return 1;
            }

            return 0;
        }
        catch (EngineException e)
        {
            await Console.Error.WriteLineAsync("obm: " + e.Message);
            return 1;
        }
    }

    internal static string Moved(MigrateSecretsResult result, string store) =>
        result.From is null ? $"Secrets will be kept in {Where(store, result)}"
        : result.From == store ? $"Secrets are already kept in {Where(store, result)}"
        : result.Moved == 0 ? $"There were no secrets to move. They'll be kept in {Where(store, result)}"
        : string.Create(CultureInfo.InvariantCulture,
            $"Moved {result.Moved} {(result.Moved == 1 ? "secret" : "secrets")} from {Where(result.From, result)} to {Where(store, result)}");

    private static string Where(string store, MigrateSecretsResult result) =>
        store == SecretStoreKind.File.Name() ? result.File : SecretStoreKinds.Parse(store)!.Value.Title();
}
