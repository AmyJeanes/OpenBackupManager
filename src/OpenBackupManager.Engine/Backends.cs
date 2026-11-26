using System.Globalization;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// The backends setup offers: those Open Backup Manager is tested with, then the rest of the bundled rclone's, hidden
// only by rclone's own ratings, so its updates change the list without a list of names here
internal sealed class Backends(AppPaths paths)
{
    // CI tests these, and local is another drive or a mounted NAS
    private static readonly string[] Tested = ["onedrive", "local"];

    private readonly Lock _lock = new();
    private Task<IReadOnlyList<BackendInfo>>? _list;

    // Asked of rclone once, and again only after it failed
    public Task<IReadOnlyList<BackendInfo>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_list is null or { IsFaulted: true })
            {
                _list = AskRcloneAsync();
            }

            return _list.WaitAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<BackendInfo>> AskRcloneAsync()
    {
        // Its names can't be a remote's id, which has no underscores, and no config, as it only lists backends
        var options = new RcdOptions(
            BundledRclone.FilePath,
            RunFolder.Socket(paths, "backends.sock"),
            "",
            Path.Combine(paths.Cache, "rclone", "_backends"),
            Path.Combine(paths.Logs, "rclone-_backends.log"),
            Remotes.UserAgent);
        RunFolder.Create(paths);
        PrivateFiles.CreateFolder(paths.Logs);

        // Not stopped by whoever asked first, as the next to ask waits on the same answer
        await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);
        var providers = (await rcd.Client.ProvidersAsync(CancellationToken.None)).Providers;
        return
        [
            .. providers
                .Where(Offered)
                .Select(provider => new BackendInfo(provider.Prefix, provider.Description, Tested.Contains(provider.Prefix), Tier(provider), Docs(provider)))
                .OrderBy(backend => backend.Tested ? Array.IndexOf(Tested, backend.Type) : Tested.Length)
                .ThenBy(backend => backend.Type, StringComparer.Ordinal),
        ];
    }

    // Not a wrapper, such as crypt, which comes later as part of setting up a remote; not an old name for another; not
    // one rclone no longer maintains (tier 5); and not one its tests can't run on, as only the read-only ones are
    private static bool Offered(Provider provider) =>
        !provider.Overview.Virtual && !provider.Hide && Tier(provider) < 5 && provider.Overview.IntegrationTests != "N/A";

    // Named as rclone names the backend, without its spaces, such as rclone.org/googlecloudstorage
    private static Uri Docs(Provider provider) => new($"https://rclone.org/{provider.Name.Replace(" ", "", StringComparison.Ordinal)}/");

    // Such as "Tier 1"
    private static int Tier(Provider provider) => int.Parse(provider.Overview.Tier.Split(' ')[^1], CultureInfo.InvariantCulture);
}
