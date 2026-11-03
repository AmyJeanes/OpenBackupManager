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

    // Null if another process holds it, or something else has its file open at that moment
    public static ProcessLock? TryTake(string name, AppPaths paths)
    {
        Directory.CreateDirectory(paths.State);
        // .NET only takes an exclusive flock on macOS and Linux when nothing is shared. Windows checks sharing itself, so
        // it lets others read, as Chromium does, and a program reading the state folder isn't taken for a second copy
        var share = OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None;
        try
        {
            return new ProcessLock(new FileStream(Path.Combine(paths.State, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, share));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _file.Dispose();
}
