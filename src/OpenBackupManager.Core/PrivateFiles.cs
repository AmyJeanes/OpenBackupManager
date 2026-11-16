namespace OpenBackupManager.Core;

// Folders and files only this user can open on macOS and Linux, as the XDG spec asks for its folders, so another local
// user can't take an flock on one and keep the engine from starting. Windows keeps each user's own folders to them
public static class PrivateFiles
{
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode Others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    // With any folders above it that aren't there yet. One that's there already is left as it is, as a folder chosen
    // as a home may be shared on purpose
    public static void CreateFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        // .NET only gives the mode to the last folder it makes
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is { } parent && !Directory.Exists(parent))
        {
            CreateFolder(parent);
        }

        Directory.CreateDirectory(path, PrivateFolder);
    }

    // With a mode that can create the file. One that's there already is made private too
    public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(path, mode, access, share);
        }

        var stream = new FileStream(path, new FileStreamOptions { Mode = mode, Access = access, Share = share, UnixCreateMode = PrivateFile });
        try
        {
            if (WithoutOthers(File.GetUnixFileMode(stream.SafeFileHandle)) is { } changed)
            {
                File.SetUnixFileMode(stream.SafeFileHandle, changed);
            }
        }
        // Best effort, as one that belongs to someone else, or is on a read-only disk, may still be usable
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return stream;
    }

    // For a new file taking another's place, which keeps who it was shared with, as that may have been chosen
    public static void CopyMode(string from, string to)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(to, File.GetUnixFileMode(from));
        }
    }

    // Null if it's private already
    private static UnixFileMode? WithoutOthers(UnixFileMode mode) => (mode & Others) == 0 ? null : mode & ~Others;
}
