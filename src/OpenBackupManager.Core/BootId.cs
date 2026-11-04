using System.Runtime.InteropServices;
using System.Text;

namespace OpenBackupManager.Core;

// Changes each time the computer starts, so a run that was still going when it restarted or lost power isn't taken for
// a crash
internal static class BootId
{
    // Null on Windows, which finds crashes in its event log instead, or if it can't be read
    public static string? Current()
    {
        try
        {
            return OperatingSystem.IsLinux() ? File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()
                : OperatingSystem.IsMacOS() ? BootSessionUuid()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Not kern.boottime, which moves whenever the clock is set, even by a second
    private static string? BootSessionUuid()
    {
        var buffer = new byte[64];
        var length = (nuint)buffer.Length;
        return NativeMethods.sysctlbyname("kern.bootsessionuuid\0"u8.ToArray(), buffer, ref length, IntPtr.Zero, 0) == 0
            ? Encoding.ASCII.GetString(buffer, 0, (int)length).TrimEnd('\0')
            : null;
    }

    private static class NativeMethods
    {
        [DllImport("libc")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int sysctlbyname(byte[] name, byte[] value, ref nuint length, IntPtr newValue, nuint newLength);
    }
}
