using Windows.Win32;

namespace OpenBackupManager.Platform.Windows;

// CsWin32 generates the declarations from NativeMethods.txt
internal static class NativeMethods
{
    // The ID Windows knows the app by, if it was given one, as Velopack does for the installed app
    public static unsafe string? ExplicitAppId()
    {
        if (PInvoke.GetCurrentProcessExplicitAppUserModelID(out var id).Failed)
        {
            return null;
        }

        try
        {
            return id.ToString();
        }
        finally
        {
            PInvoke.CoTaskMemFree(id);
        }
    }
}
