using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// Folders on a remote, by paths as rclone takes them, found a folder at a time from the top, so where the remote
// ignores case each part takes its real name. What a full path starts with, such as / or a drive, is kept as given.
// FullPathsOnly is for local, where anything else would be from the folder the engine happened to start in
internal sealed partial class RemoteFolders(RcClient rclone, string remote, bool fullPathsOnly)
{
    private const string Backup = ".obm";

    private FsFeatures? _features;

    // As far as it exists, with what's in it when all of it does
    public async Task<FoundFolder> FindAsync(string path, CancellationToken cancellationToken)
    {
        if (fullPathsOnly && !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(OperatingSystem.IsWindows()
                ? @"Give the full path, from a drive such as C:\ or a share such as \\server\share\"
                : "Give the full path, from /");
        }

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
        var aboveUnlisted = false;
        var missing = false;
        // Not in the listing above, so only there if that listing hid it
        var unseen = false;
        IReadOnlyList<ListItem>? items = null;
        for (var level = 0; ; level++)
        {
            var root = level == parts.Length;
            if (missing)
            {
                items = null;
            }
            else
            {
                try
                {
                    items = await rclone.ListAsync(remote + top + string.Join(separator, found), "", dirsOnly: !root, cancellationToken);
                    missing = items is null;
                    // As local's does, listing a folder it can't open as empty
                    if (unseen && !missing)
                    {
                        aboveUnlisted = true;
                        nearMiss = null;
                    }
                }
                catch (RcloneException) when (!root)
                {
                    // Such as the top of an S3 key kept to one bucket, or an SFTP folder that can only be passed
                    // through. The root itself still has to list
                    items = null;
                    aboveUnlisted = true;
                }
            }

            if (root)
            {
                break;
            }

            var part = parts[level];
            var folders = items?.Where(item => item.IsDir).Select(item => item.Name).Order(StringComparer.Ordinal).ToList();
            if (folders?.Contains(Backup) == true)
            {
                backupAbove ??= Joined(top, found, separator);
            }

            // As given before another form of it, as a remote that minds case can hold both
            var name = folders?.FirstOrDefault(folder => folder == part)
                ?? folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: false))
                ?? (_features.CaseInsensitive ? folders?.FirstOrDefault(folder => Same(folder, part, ignoreCase: true)) : null);
            unseen = name is null && folders is not null;
            if (name is null && folders is not null)
            {
                nearMiss = Nearest(folders, part) is { } near
                    ? Joined(top, [.. found, near, .. parts[(found.Count + 1)..]], separator)
                    : null;
            }

            found.Add(name ?? part);
        }

        return new FoundFolder(
            Joined(top, found, separator),
            items?.Where(item => item.IsDir).Select(item => item.Name).Order(StringComparer.Ordinal).ToList(),
            items?.Count(item => !item.IsDir) ?? 0,
            backupAbove,
            nearMiss,
            aboveUnlisted);
    }

    // That it can hold files, by writing one, checking its size and deleting it, making the folder if it isn't there
    public async Task WriteTestAsync(string path, CancellationToken cancellationToken)
    {
        var fs = remote + path;
        var name = ".obm-write-test-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var content = "Open Backup Manager checks it can write here, then deletes this\n"u8.ToArray();
        await rclone.MkdirAsync(fs, "", cancellationToken);
        var failed = true;
        try
        {
            // A write that fails may still leave the file
            await rclone.UploadFileAsync(fs, "", name, content, cancellationToken);
            if ((await rclone.StatAsync(fs, name, cancellationToken))?.Size != content.Length)
            {
                throw new RcloneException("the test file written there was missing or cut short");
            }

            failed = false;
        }
        finally
        {
            await DeleteTestFileAsync(fs, name, quietly: failed);
        }
    }

    // Even once what asked has gone, though not for long, as the setup's rclone stops once the check returns. Quietly
    // after a failure, such as when the file never got there, which would hide why
    private async Task DeleteTestFileAsync(string fs, string name, bool quietly)
    {
        using var deleting = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await rclone.DeleteFileAsync(fs, name, deleting.Token);
        }
        catch (Exception e) when (quietly && e is RcloneException or HttpRequestException or IOException or OperationCanceledException)
        {
        }
    }

    private static string Joined(string top, List<string> parts, char separator) =>
        top + string.Join(separator, parts) + (parts.Count > 0 ? separator : "");

    // macOS and Windows can write the same accented name in two forms
    private static bool Same(string folder, string part, bool ignoreCase) =>
        string.Equals(folder.Normalize(NormalizationForm.FormC), part.Normalize(NormalizationForm.FormC), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // The closest a slip of a letter or two away, or of case where the remote minds it. Fewer for a short name,
    // where that many would make it another name altogether
    private static string? Nearest(List<string> folders, string part)
    {
        var folded = Folded(part);
        var most = Math.Min(2, (folded.Length - 1) / 2);
        return folders.Select(folder => (Folder: folder, Distance: Distance(Folded(folder), folded)))
            .Where(near => near.Distance <= most)
            .OrderBy(near => near.Distance)
            .Select(near => near.Folder)
            .FirstOrDefault();
    }

    private static string Folded(string name) => name.Normalize(NormalizationForm.FormC).ToUpperInvariant();

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
// top. Folders are those directly in it, or null if it isn't there. BackupAbove is the highest folder above it holding
// a backup's .obm, and NearMiss is a path that's there for one that isn't, that it may have been meant to be.
// AboveUnlisted is when a folder above couldn't be listed, so a backup or case there wasn't seen
internal sealed record FoundFolder(
    string Path,
    IReadOnlyList<string>? Folders,
    int Files,
    string? BackupAbove,
    string? NearMiss,
    bool AboveUnlisted)
{
    public bool HasBackup => Folders?.Contains(".obm") == true;
}
