using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OpenBackupManager.Core.Protocol;

/// <summary>
/// The engine's private folder for Unix sockets: its own, except on Windows, and each rcd's
/// </summary>
public static class RunFolder
{
    // So a --home with room for one has room for them all
    private const int LongestName = 16;

    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static string Path(AppPaths paths) => System.IO.Path.Combine(paths.State, "run");

    public static string Socket(AppPaths paths, string name)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, LongestName, nameof(name));
        return System.IO.Path.Combine(Path(paths), name);
    }

    // Only this user can open it, as its sockets take no password. One made first by something else is made so too,
    // unless it's a link, which could lead anywhere
    public static void Create(AppPaths paths)
    {
        var folder = Path(paths);
        PrivateFiles.CreateFolder(paths.State);
        if (OperatingSystem.IsWindows())
        {
            CreateOnWindows(folder);
            return;
        }

        if (Directory.CreateDirectory(folder, Private).LinkTarget is not null)
        {
            throw new EngineException($"{folder} is a link, so the engine won't keep its sockets there");
        }

        try
        {
            File.SetUnixFileMode(folder, Private);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new EngineException(BelongsToAnother(folder), e);
        }
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

    // Rather than what it would inherit, which outside the user's own folders, as on another drive, often lets every
    // user in
    [SupportedOSPlatform("windows")]
    private static void CreateOnWindows(string folder)
    {
        using var user = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user.User!,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        var info = new DirectoryInfo(folder);
        if (!info.Exists)
        {
            info.Create(security);
            return;
        }

        if (info.LinkTarget is not null)
        {
            throw new EngineException($"{folder} is a link, so the engine won't keep its sockets there");
        }

        // Its owner can always let themselves back in. One this user made has their token's owner, maybe Administrators
        var owner = info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        if (owner != user.User && owner != user.Owner)
        {
            throw new EngineException(BelongsToAnother(folder));
        }

        try
        {
            info.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new EngineException(BelongsToAnother(folder), e);
        }
    }

    private static string BelongsToAnother(string folder) => $"{folder} belongs to another user, so the engine can't keep its sockets there";
}
