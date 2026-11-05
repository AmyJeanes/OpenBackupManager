using System.ComponentModel;
using System.Diagnostics;

namespace OpenBackupManager.Cli;

// Opens links in the browser of the person at the computer
internal static class Browser
{
    // Over SSH, macOS opens it on the Mac's own screen, and on Linux with no display xdg-open can start a text browser
    // in the terminal. Stripe's CLI checks for SSH, and gcloud and Python's webbrowser for a display
    public static bool CanOpen(Func<string, string?> environment) =>
        string.IsNullOrEmpty(environment("SSH_CONNECTION"))
        && string.IsNullOrEmpty(environment("SSH_TTY"))
        && (!OperatingSystem.IsLinux() || !string.IsNullOrEmpty(environment("DISPLAY")) || !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY")));

    // False if it couldn't, as when xdg-open has nothing to open it with. xdg-open can wait for a browser it starts,
    // so one still running after a few seconds counts as opened
    public static bool TryOpen(string link)
    {
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(link) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", [link]);
        try
        {
            using var process = Process.Start(start);
            return process is null || !process.WaitForExit(TimeSpan.FromSeconds(5)) || process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
