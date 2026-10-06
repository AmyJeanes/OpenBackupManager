using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

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

    // The display's scaling, where 1 is 100% (96 DPI)
    public static double Scale(DisplayArea display)
    {
        _ = PInvoke.GetDpiForMonitor((HMONITOR)Win32Interop.GetMonitorFromDisplayId(display.DisplayId), MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _);
        return dpi / 96.0;
    }

    // Waits until Windows has next updated the screen
    public static void DwmFlush() => _ = PInvoke.DwmFlush();

    public static void PlaceBelowTaskbar(IntPtr hwnd) =>
        _ = PInvoke.SetWindowPos((HWND)hwnd, PInvoke.FindWindow("Shell_TrayWnd", null), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    // Windows identifies a tray icon by the window that owns it and its ID, and WinUIEx keeps that window private,
    // so this asks with each of this thread's windows until one owns it.
    // Replace with TrayIcon.GetBounds once https://github.com/dotMorten/WinUIEx/issues/281 is released
    public static RectInt32? TrayIconBounds(uint id)
    {
        RectInt32? bounds = null;
        _ = PInvoke.EnumThreadWindows(PInvoke.GetCurrentThreadId(), (hwnd, _) =>
        {
            var icon = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = hwnd, uID = id };
            if (PInvoke.Shell_NotifyIconGetRect(in icon, out var rect).Failed)
            {
                return true;
            }

            bounds = new RectInt32(rect.left, rect.top, rect.Width, rect.Height);
            return false;
        }, default);
        return bounds;
    }
}
