using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace OpenBackupManager.App.Windows.UITests;

// Windows APIs that FlaUI doesn't cover. CsWin32 generates the declarations from NativeMethods.txt
internal static class NativeMethods
{
    // A cloaked window is drawn as usual but not put on screen
    public static bool IsCloaked(Window window)
    {
        var cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(Handle(window), DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, MemoryMarshal.AsBytes(new Span<int>(ref cloaked))).Succeeded
            && cloaked != 0;
    }

    // Windows only lets the process that had the last input bring a window to the front, so this presses Alt first.
    // Alt is let go once the window is in front, so the window that had focus doesn't take it as a menu key
    public static void BringToFront(Window window)
    {
        var hwnd = Handle(window);
        Keyboard.Press(VirtualKeyShort.ALT);
        _ = PInvoke.SetForegroundWindow(hwnd);
        Keyboard.Release(VirtualKeyShort.ALT);
        if (PInvoke.GetForegroundWindow() != hwnd)
        {
            throw new InvalidOperationException("Couldn't bring the window to the front");
        }
    }

    private static HWND Handle(Window window) => (HWND)window.Properties.NativeWindowHandle.Value;
}
