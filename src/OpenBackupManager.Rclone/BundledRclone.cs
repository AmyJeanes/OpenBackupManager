using System.Reflection;

namespace OpenBackupManager.Rclone;

/// <summary>
/// The rclone binary the build puts next to the app, at the version pinned in rclone.version
/// </summary>
public static class BundledRclone
{
    public static string Version => typeof(BundledRclone).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "RcloneVersion")
        .Value ?? throw new InvalidOperationException("The build didn't record the bundled rclone version");

    // Named for the app, so it can't stand in for the user's own rclone if it ends up on PATH, and shows as ours among
    // their processes
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "obm-rclone.exe" : "obm-rclone");
}
