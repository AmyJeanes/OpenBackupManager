using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Recovery;

namespace OpenBackupManager.Platform.Windows;

// Asks Windows to start the app again if it crashes or stops responding. Windows only does so once the app has run for
// 60 seconds, so a copy that crashes as it starts isn't restarted over and over
public static class CrashRestart
{
    // Not after an update or a restart of Windows, which aren't crashes
    public static void Register(string arguments) =>
        Marshal.ThrowExceptionForHR(PInvoke.RegisterApplicationRestart(
            arguments,
            REGISTER_APPLICATION_RESTART_FLAGS.RESTART_NO_PATCH | REGISTER_APPLICATION_RESTART_FLAGS.RESTART_NO_REBOOT));
}
