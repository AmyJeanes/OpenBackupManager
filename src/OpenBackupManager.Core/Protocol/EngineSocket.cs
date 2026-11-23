namespace OpenBackupManager.Core.Protocol;

/// <summary>
/// Where the engine listens for the tray app and command line app: a named pipe on Windows, and a Unix domain socket
/// in its run folder elsewhere, beside the engine's lock, which is the same in every session
/// </summary>
public static class EngineSocket
{
    // As System.IO.Pipes takes it: a pipe's name on Windows, and the socket's full path elsewhere
    public static string Name(AppPaths paths) =>
        OperatingSystem.IsWindows() ? AppHome.InstanceKey(paths.State) + "-engine" : RunFolder.Socket(paths, "engine.sock");
}
