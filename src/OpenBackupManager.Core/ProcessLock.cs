namespace OpenBackupManager.Core;

/// <summary>
/// A lock only one process at a time can hold for each home, such as the engine's: a file in the state folder that's
/// kept open exclusively. The OS releases it if the process dies, so a crash never leaves it held. It's the same file
/// whatever the session, and however the home's path is written, such as through a junction or a short name
/// </summary>
public sealed class ProcessLock : IDisposable
{
    private readonly FileStream _file;

    private ProcessLock(FileStream file) => _file = file;

    // Null if another process holds it, or on Windows something else has its file open at that moment. Any other error,
    // such as a read-only disk, is thrown, so it isn't taken for a running copy
    public static ProcessLock? TryTake(string name, AppPaths paths)
    {
        PrivateFiles.CreateFolder(paths.State);
        // .NET only takes an exclusive flock on macOS and Linux when nothing is shared. Windows checks sharing itself,
        // so it lets others read, as Chromium's process_singleton_win.cc does, and a program reading the state folder
        // isn't taken for a second copy. Defender's scans never held it in a probe of about 140,000 opens (Nov 2026)
        var share = OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;
        try
        {
            // Private, or another user could hold it and pass for a running copy
            return new ProcessLock(PrivateFiles.Open(Path.Combine(paths.State, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, share));
        }
        catch (IOException e) when (IsHeld(e))
        {
            return null;
        }
    }

    // flock's EWOULDBLOCK, whose number differs by OS, or Windows' ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION
    private static bool IsHeld(IOException e) => OperatingSystem.IsWindows()
        ? e.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
        : e.HResult == (OperatingSystem.IsMacOS() ? 35 : 11);

    public void Dispose() => _file.Dispose();
}
