using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

// A window that's just been shown is blank until WinUI's first frame reaches the screen, and controls such as the title bar
// lay themselves out again a frame or two after that
internal static class FirstDraw
{
    // Keeps a new window cloaked until it's drawn
    public static void HideUntilDrawn(Window window)
    {
        NativeMethods.Cloak(window.GetWindowHandle(), true);
        WhenDrawn(window, () => NativeMethods.Cloak(window.GetWindowHandle(), false));
    }

    // Once the content has loaded and a frame has gone by with no layout changes
    public static void WhenDrawn(Window window, Action drawn)
    {
        var content = (FrameworkElement)window.Content;
        var laidOut = true;
        void OnLayoutUpdated(object? sender, object e) => laidOut = true;

        void OnRendering(object? sender, object e)
        {
            if (!content.IsLoaded || laidOut)
            {
                laidOut = false;
                return;
            }

            CompositionTarget.Rendering -= OnRendering;
            content.LayoutUpdated -= OnLayoutUpdated;
            // WinUI has drawn the settled frame, and this waits for Windows to put it on screen
            NativeMethods.DwmFlush();
            drawn();
        }

        content.LayoutUpdated += OnLayoutUpdated;
        CompositionTarget.Rendering += OnRendering;
    }
}
