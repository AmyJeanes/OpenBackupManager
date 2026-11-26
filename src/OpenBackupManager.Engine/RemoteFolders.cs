using System.Security.Cryptography;
using System.Text;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// Folders on a remote, by paths as rclone takes them, found a folder at a time from the top, so where the remote
// ignores case each part takes its real name. A leading / or drive, as SFTP and local use for a full path, stays
internal sealed class RemoteFolders(RcClient rclone, string remote)
{
    private const string Backup = ".obm";

    private bool? _ignoresCase;

    // As far as it exists, with what's in it when all of it does
    public async Task<FoundFolder> FindAsync(string path, CancellationToken cancellationToken)
    {
        var (top, parts) = Split(path);
        _ignoresCase ??= (await rclone.FsInfoAsync(remote + top, cancellationToken)).Features.CaseInsensitive;

        var found = new List<string>();
        string? backupAbove = null;
        string? nearMiss = null;
        var items = await rclone.ListAsync(remote + top, "", dirsOnly: false, cancellationToken);
        foreach (var part in parts)
        {
            var folders = items?.Where(item => item.IsDir).Select(item => item.Name).Order(StringComparer.Ordinal).ToList();
            if (folders?.Contains(Backup) == true)
            {
                backupAbove ??= Joined(top, found);
            }

            var name = folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: false))
                ?? (_ignoresCase.Value ? folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: true)) : null);
            if (name is null && folders is not null)
            {
                nearMiss = folders.FirstOrDefault(folder => Near(folder, part)) is { } near
                    ? Joined(top, [.. found, near, .. parts[(found.Count + 1)..]])
                    : null;
            }

            found.Add(name ?? part);
            items = name is null ? null : await rclone.ListAsync(remote + top + string.Join('/', found), "", dirsOnly: false, cancellationToken);
        }

        return new FoundFolder(
            Joined(top, found),
            items?.Where(item => item.IsDir).Select(item => item.Name).Order(StringComparer.Ordinal).ToList(),
            items?.Count(item => !item.IsDir) ?? 0,
            backupAbove,
            nearMiss);
    }

    // That it can hold files, by writing one, reading it back and deleting it, which makes the folder if it isn't there
    public async Task WriteTestAsync(string path, CancellationToken cancellationToken)
    {
        var fs = remote + path;
        var name = ".obm-write-test-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var content = "Open Backup Manager checks it can write here, then deletes this\n"u8.ToArray();
        await rclone.MkdirAsync(fs, "", cancellationToken);
        await rclone.UploadFileAsync(fs, "", name, content, cancellationToken);
        try
        {
            if ((await rclone.StatAsync(fs, name, cancellationToken))?.Size != content.Length)
            {
                throw new RcloneException("What was written couldn't be read back");
            }
        }
        finally
        {
            await rclone.DeleteFileAsync(fs, name, cancellationToken);
        }
    }

    private static string Joined(string top, List<string> parts) => top + string.Join('/', parts) + (parts.Count > 0 ? "/" : "");

    // macOS and Windows can write the same accented name in two forms
    private static bool Same(string folder, string part, bool ignoreCase) =>
        string.Equals(folder.Normalize(NormalizationForm.FormC), part.Normalize(NormalizationForm.FormC), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // A slip of a letter or two, or of case where the remote minds it
    private static bool Near(string folder, string part) =>
        Distance(folder.Normalize(NormalizationForm.FormC).ToUpperInvariant(), part.Normalize(NormalizationForm.FormC).ToUpperInvariant()) <= 2;

    // How many letters to add, take away or change to make one the other
    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }

        return row[b.Length];
    }

    private static (string Top, string[] Parts) Split(string path)
    {
        var top = path.StartsWith('/') ? "/" : path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/' ? path[..3] : "";
        return (top, path[top.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}

// Path is as far as it exists in the remote's own case, then as asked, ending in / unless it's the remote's top.
// Folders are those directly in it, or null when it isn't there. BackupAbove is the highest folder above it holding a
// backup's .obm, and NearMiss is a path that's there for one that isn't, that it may have been meant to be
internal sealed record FoundFolder(string Path, IReadOnlyList<string>? Folders, int Files, string? BackupAbove, string? NearMiss)
{
    public bool HasBackup => Folders?.Contains(".obm") == true;
}
