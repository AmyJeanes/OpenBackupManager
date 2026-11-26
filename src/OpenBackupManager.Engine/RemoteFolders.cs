using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// Folders on a remote, by paths as rclone takes them, found a folder at a time from the top, so where the remote
// ignores case each part takes its real name. What a full path starts with, such as / or a drive, is kept as given
internal sealed partial class RemoteFolders(RcClient rclone, string remote)
{
    private const string Backup = ".obm";

    private FsFeatures? _features;

    // As far as it exists, with what's in it when all of it does
    public async Task<FoundFolder> FindAsync(string path, CancellationToken cancellationToken)
    {
        _features ??= (await rclone.FsInfoAsync(remote, cancellationToken)).Features;
        var (top, parts, separator) = Split(path, _features.IsLocal && OperatingSystem.IsWindows());
        // The walk would take them as folders, where rclone takes them as steps
        if (parts.Any(part => part is "." or ".."))
        {
            throw new ArgumentException("Give the path without . or ..");
        }

        var found = new List<string>();
        string? backupAbove = null;
        string? nearMiss = null;
        var items = await rclone.ListAsync(remote + top, "", dirsOnly: false, cancellationToken);
        foreach (var part in parts)
        {
            var folders = items?.Where(item => item.IsDir).Select(item => item.Name).Order(StringComparer.Ordinal).ToList();
            if (folders?.Contains(Backup) == true)
            {
                backupAbove ??= Joined(top, found, separator);
            }

            var name = folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: false))
                ?? (_features.CaseInsensitive ? folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: true)) : null);
            if (name is null && folders is not null)
            {
                nearMiss = folders.FirstOrDefault(folder => Near(folder, part)) is { } near
                    ? Joined(top, [.. found, near, .. parts[(found.Count + 1)..]], separator)
                    : null;
            }

            found.Add(name ?? part);
            items = name is null ? null : await rclone.ListAsync(remote + top + string.Join(separator, found), "", dirsOnly: false, cancellationToken);
        }

        return new FoundFolder(
            Joined(top, found, separator),
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

    private static string Joined(string top, List<string> parts, char separator) =>
        top + string.Join(separator, parts) + (parts.Count > 0 ? separator : "");

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

    // Only Windows' own paths take \ for /, as elsewhere it can be in a name. Joined as they were given
    private static (string Top, string[] Parts, char Separator) Split(string path, bool windows)
    {
        var top = (windows ? WindowsTop() : Top()).Match(path).Value;
        var separator = windows && path.FirstOrDefault(c => c is '/' or '\\') == '\\' ? '\\' : '/';
        var parts = path[top.Length..].Split(windows ? ['/', '\\'] : ['/'], StringSplitOptions.RemoveEmptyEntries);
        // rclone can hang on a share with nothing after it
        return (top.Length > 0 && top[^1] is not ('/' or '\\') ? top + separator : top, parts, separator);
    }

    // As SFTP and local take a full path
    [GeneratedRegex(@"\A(?:[A-Za-z]:/|/)")]
    private static partial Regex Top();

    // Also from a \\server\share
    [GeneratedRegex(@"\A(?:[\\/]{2}[^\\/]+[\\/][^\\/]+[\\/]?|[A-Za-z]:[\\/]|[\\/])")]
    private static partial Regex WindowsTop();
}

// Path is as far as it exists in the remote's own case, then as asked, ending in a separator unless it's the remote's
// top. Folders are those directly in it, or null when it isn't there. BackupAbove is the highest folder above it holding
// a backup's .obm, and NearMiss is a path that's there for one that isn't, that it may have been meant to be
internal sealed record FoundFolder(string Path, IReadOnlyList<string>? Folders, int Files, string? BackupAbove, string? NearMiss)
{
    public bool HasBackup => Folders?.Contains(".obm") == true;
}
