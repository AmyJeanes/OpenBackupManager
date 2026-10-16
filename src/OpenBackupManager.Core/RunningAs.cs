using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace OpenBackupManager.Core;

/// <summary>
/// Whether obm and obmd were started with rights other than the user's own. The engine runs as the user with their
/// normal rights, so the tray app and obm can always reach it, and its files belong to them
/// </summary>
public static class RunningAs
{
    private const int TokenElevationType = 18;
    private const int TokenElevationTypeFull = 2;

    // Such as from a terminal opened as administrator. Where the admin token is the only one, as with UAC off, the
    // built-in Administrator or SSH, there's no normal token to run with instead
    [SupportedOSPlatform("windows")]
    public static bool AdministratorWithNormalTokenToo()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!NativeMethods.GetTokenInformation(identity.Token, TokenElevationType, out var type, sizeof(int), out _))
        {
            throw new Win32Exception();
        }

        return type == TokenElevationTypeFull;
    }

    // Through sudo, doas, pkexec or run0, which say who ran them. Real root, as with a root login, su - or a container,
    // has none of these
    [UnsupportedOSPlatform("windows")]
    public static bool SwitchedFromAnotherUser() =>
        SwitchedFromAnotherUser(Environment.GetEnvironmentVariable, NativeMethods.geteuid(), Environment.UserName);

    // run0 sets SUDO_UID too
    internal static bool SwitchedFromAnotherUser(Func<string, string?> environment, uint userId, string userName)
    {
        var id = userId.ToString(CultureInfo.InvariantCulture);
        return environment("SUDO_UID") is { } sudo && sudo != id
            || environment("PKEXEC_UID") is { } pkexec && pkexec != id
            || environment("DOAS_USER") is { } doas && doas != userName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int length, out int returnLength);

        [DllImport("libc")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern uint geteuid();
    }
}
