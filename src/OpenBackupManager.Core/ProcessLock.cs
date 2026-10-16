namespace OpenBackupManager.Core;

/// <summary>
/// A lock only one process at a time can hold for each home, such as the engine's. The OS releases it if the process
/// dies, so a crash never leaves it held. A named mutex on Windows, and an flock on a file in the state folder elsewhere,
/// which is the same in every session, whatever its environment
/// </summary>
public sealed class ProcessLock : IDisposable
{
    private readonly IDisposable _held;

    private ProcessLock(IDisposable held) => _held = held;

    // Null if another process holds it
    public static ProcessLock? TryTake(string name, AppPaths paths)
    {
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(false, $"{AppHome.InstanceKey(paths.State)}-{name}");
            try
            {
                if (!mutex.WaitOne(0))
                {
                    mutex.Dispose();
                    return null;
                }
            }
            catch (AbandonedMutexException)
            {
                // Its last holder ended without releasing it, and it's this process's now
            }

            return new ProcessLock(mutex);
        }

        Directory.CreateDirectory(paths.State);
        try
        {
            // .NET takes an exclusive flock for FileShare.None on macOS and Linux
            return new ProcessLock(new FileStream(Path.Combine(paths.State, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    // A mutex is only closed, not released, as only the thread that took it can release it. If another process still has
    // it open, Windows treats it as abandoned, which TryTake allows for
    public void Dispose() => _held.Dispose();
}
