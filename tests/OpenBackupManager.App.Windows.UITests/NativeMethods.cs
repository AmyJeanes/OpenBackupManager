using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

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
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground != hwnd)
        {
            throw new InvalidOperationException($"Couldn't bring the window to the front, which {ProcessName(foreground)} has");
        }
    }

    public static nint TopLevelWindow(AutomationElement element) => PInvoke.GetAncestor(Handle(element), GET_ANCESTOR_FLAGS.GA_ROOT);

    public static int ProcessId(nint hwnd)
    {
        _ = PInvoke.GetWindowThreadProcessId((HWND)hwnd, out var id);
        return (int)id;
    }

    // Windows identifies a tray icon by the window that owns it and its ID, so this asks with each of the process's
    // windows until one owns it. Unlike the icon's name, that can't match an icon left behind by a copy that was ended
    public static Rectangle? TrayIconBounds(int processId, uint id)
    {
        Rectangle? bounds = null;
        _ = PInvoke.EnumWindows((hwnd, _) =>
        {
            var icon = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = hwnd, uID = id };
            if (ProcessId(hwnd) != processId || PInvoke.Shell_NotifyIconGetRect(in icon, out var rect).Failed)
            {
                return true;
            }

            bounds = new Rectangle(rect.left, rect.top, rect.Width, rect.Height);
            return false;
        }, default);
        return bounds;
    }

    private static string ProcessName(HWND hwnd)
    {
        try
        {
            return Process.GetProcessById(ProcessId(hwnd)).ProcessName;
        }
        catch (ArgumentException)
        {
            return "a process that has exited";
        }
    }

    private static HWND Handle(AutomationElement element) => (HWND)element.Properties.NativeWindowHandle.Value;
}
