using System.Runtime.InteropServices;

namespace OpenBackupManager.Core;

public static class CurrentOS
{
    public static OSPlatform Platform =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OperatingSystem.IsLinux() ? OSPlatform.Linux
        : throw new PlatformNotSupportedException("Open Backup Manager runs on Windows, macOS and Linux");
}
