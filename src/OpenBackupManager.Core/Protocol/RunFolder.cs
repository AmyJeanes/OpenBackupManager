using System.Net.Sockets;

namespace OpenBackupManager.Core.Protocol;

/// <summary>
/// The engine's private folder for Unix sockets: its own, except on Windows, and each rcd's
/// </summary>
public static class RunFolder
{
    // So a --home with room for one has room for them all
    private const int LongestName = 16;

    public static string Path(AppPaths paths) => System.IO.Path.Combine(paths.State, "run");

    public static string Socket(AppPaths paths, string name)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, LongestName, nameof(name));
        return System.IO.Path.Combine(Path(paths), name);
    }

    // A socket's path is limited to about 104 bytes on macOS and 108 on Linux and Windows
    public static void CheckRoom(AppPaths paths)
    {
        try
        {
            _ = new UnixDomainSocketEndPoint(Socket(paths, new string('x', LongestName)));
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new EngineException(
                $"The engine's sockets, in {Path(paths)}, would be a longer path than this OS allows. Use a --home folder with a shorter path", e);
        }
    }
}
