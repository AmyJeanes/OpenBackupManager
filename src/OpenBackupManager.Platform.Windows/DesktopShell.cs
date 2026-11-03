using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Com;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace OpenBackupManager.Platform.Windows;

// Starts a program as the user with their normal rights, from a process that has admin rights, which it can't give up.
// It asks the desktop's Explorer to start it, as https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643 shows
public static class DesktopShell
{
    // False when the desktop's Explorer isn't running as this user, or couldn't start it
    public static bool TryStart(string file, string arguments)
    {
        if (!ShellIsThisUsers())
        {
            return false;
        }

        var fileText = Marshal.StringToBSTR(file);
        try
        {
            PInvoke.CoCreateInstance<IShellWindows>(typeof(ShellWindows).GUID, null, CLSCTX.CLSCTX_LOCAL_SERVER, out var windows).ThrowOnFailure();
            var desktop = windows.FindWindowSW(
                ComVariant.Create(0),
                default,
                ShellWindowTypeConstants.SWC_DESKTOP,
                out _,
                ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
            if (desktop is null)
            {
                return false;
            }

            ((ComServiceProvider)desktop).QueryService(PInvoke.SID_STopLevelBrowser, out IShellBrowser browser);
            browser.QueryActiveShellView(out var view);
            // The view only gives out IDispatch for this, not the folder view's own interface
            view.GetItemObject(_SVGIO.SVGIO_BACKGROUND, out IDispatch dispatch);
            var folderView = (IShellFolderViewDual)dispatch;

            // So the program can open its window in front, as this one could since the user just started it
            PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
            using var argumentsText = ComVariant.Create(arguments);
            ((IShellDispatch2)folderView.get_Application()).ShellExecute((BSTR)fileText, argumentsText, default, default, default);
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeBSTR(fileText);
        }
    }

    // Not when this process runs as another user, as with Run as different user, when Explorer would start it as them
    private static bool ShellIsThisUsers()
    {
        var shell = PInvoke.GetShellWindow();
        if (shell.IsNull || PInvoke.GetWindowThreadProcessId(shell, out var processId) == 0)
        {
            return false;
        }

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid || !PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return false;
        }

        using (token)
        {
            using var shellUser = new WindowsIdentity(token.DangerousGetHandle());
            using var user = WindowsIdentity.GetCurrent();
            return shellUser.User == user.User;
        }
    }
}
