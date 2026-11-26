using System.Text;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// Folders on a remote, by paths as rclone takes them, found a folder at a time from the top, so where the remote
// ignores case each part takes its real name. A leading / or drive, as SFTP and local use for a full path, stays
internal sealed class RemoteFolders(RcClient rclone, string remote)
{
    private bool? _ignoresCase;

    // As far as it exists, with the folders in it when all of it does
    public async Task<FoundFolder> FindAsync(string path, CancellationToken cancellationToken)
    {
        var (top, parts) = Split(path);
        _ignoresCase ??= (await rclone.FsInfoAsync(remote + top, cancellationToken)).Features.CaseInsensitive;

        var found = new List<string>();
        var folders = await FoldersAsync(remote + top, cancellationToken);
        foreach (var part in parts)
        {
            var name = folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: false))
                ?? (_ignoresCase.Value ? folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: true)) : null);
            found.Add(name ?? part);
            folders = name is null ? null : await FoldersAsync(remote + top + string.Join('/', found), cancellationToken);
        }

        return new FoundFolder(top + string.Join('/', found) + (found.Count > 0 ? "/" : ""), folders);
    }

    private async Task<IReadOnlyList<string>?> FoldersAsync(string fs, CancellationToken cancellationToken) =>
        (await rclone.ListAsync(fs, "", dirsOnly: true, cancellationToken))?.Select(item => item.Name).Order(StringComparer.Ordinal).ToList();

    // macOS and Windows can write the same accented name in two forms
    private static bool Same(string folder, string part, bool ignoreCase) =>
        string.Equals(folder.Normalize(NormalizationForm.FormC), part.Normalize(NormalizationForm.FormC), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static (string Top, string[] Parts) Split(string path)
    {
        var top = path.StartsWith('/') ? "/" : path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/' ? path[..3] : "";
        return (top, path[top.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}

// Path is as far as it exists in the remote's own case, then as asked, ending in / unless it's the remote's top.
// Folders are those directly in it, or null when it isn't there
internal sealed record FoundFolder(string Path, IReadOnlyList<string>? Folders);
