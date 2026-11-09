namespace OpenBackupManager.Core;

// A program by a full path: given a bare name, Windows, and .NET on macOS and Linux, look in the program's own folder
// and the current one before PATH, so a program of that name there would run instead, and on macOS and Linux a deleted
// current folder throws
// (https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Unix.cs#L688)
public static class SystemPrograms
{
    private static readonly string[] UnixFolders = ["/usr/bin", "/bin", "/usr/sbin", "/sbin"];

    // NixOS's root-owned programs folder
    private static readonly string[] LinuxFolders = [.. UnixFolders, "/run/current-system/sw/bin"];

    // Linux looks in the root-owned folders first, so a user's own PATH folder such as ~/.local/bin can't stand in for
    // them, then in PATH's for other distros' layouts, skipping relative ones such as `.`. Not found: a path that
    // doesn't exist, so starting it fails as an unknown name does
    public static string Find(string name)
    {
        var folders = OperatingSystem.IsWindows() ? [Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows)]
            : OperatingSystem.IsMacOS() ? UnixFolders
            : LinuxFolders.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Where(Path.IsPathRooted));
        return folders.Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists)
            ?? Path.Combine(OperatingSystem.IsWindows() ? Environment.SystemDirectory : "/usr/bin", name);
    }
}
