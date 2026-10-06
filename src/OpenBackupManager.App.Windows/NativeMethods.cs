using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace OpenBackupManager.App.Windows;

// Windows APIs that WinUI and WinUIEx don't cover. CsWin32 generates the declarations from NativeMethods.txt
internal static class NativeMethods
{
    // A cloaked window is drawn as usual but not put on screen
    public static void Cloak(IntPtr hwnd, bool cloak)
    {
        var value = cloak ? 1 : 0;
        _ = PInvoke.DwmSetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAK, MemoryMarshal.AsBytes(new ReadOnlySpan<int>(in value)));
    }

    // Waits until Windows has next updated the screen
    public static void DwmFlush() => _ = PInvoke.DwmFlush();
}
