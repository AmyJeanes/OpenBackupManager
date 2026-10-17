using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;

namespace OpenBackupManager.Platform.Windows;

// Starts a program as the user with their normal rights, from a process that has admin rights, which it can't give up.
// It asks the desktop's Explorer to start it, as https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643 shows
public static partial class DesktopShell
{
    private const uint ClassContextLocalServer = 4;
    private static readonly Guid ShellWindowsClass = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    // False when the desktop's Explorer isn't running as this user, or couldn't start it
    public static bool TryStart(string file, string arguments)
    {
        if (!ShellIsThisUsers())
        {
            return false;
        }

        var argumentsText = Marshal.StringToBSTR(arguments);
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(ShellWindowsClass, 0, ClassContextLocalServer, typeof(IShellWindows).GUID, out var windows));
            var desktop = windows.FindWindowSW(
                new Variant { Type = Variant.Int },
                default,
                (int)ShellWindowTypeConstants.SWC_DESKTOP,
                out _,
                (int)ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
            if (desktop is null)
            {
                return false;
            }

            desktop.QueryService(PInvoke.SID_STopLevelBrowser, typeof(IShellBrowser).GUID, out var browser);
            browser.QueryActiveShellView().GetItemObject((uint)_SVGIO.SVGIO_BACKGROUND, typeof(IDispatch).GUID, out var view);

            // So the program can open its window in front, as this one could since the user just started it
            PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
            view.Application().ShellExecute(file, new Variant { Type = Variant.Text, Value = argumentsText }, default, default, default);
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeBSTR(argumentsText);
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

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint classContext, in Guid iid, out IShellWindows instance);

    // VARIANT, holding nothing, a number or text
    [StructLayout(LayoutKind.Sequential)]
    internal struct Variant
    {
        public const ushort Int = 3;
        public const ushort Text = 8;

        public ushort Type;
        private readonly ushort _reserved1;
        private readonly ushort _reserved2;
        private readonly ushort _reserved3;
        public nint Value;
        private readonly nint _record;
    }

    // Only the methods called are declared fully. The others hold their places, in the order Windows has them

    [GeneratedComInterface]
    [Guid("00020400-0000-0000-C000-000000000046")]
    internal partial interface IDispatch
    {
        void GetTypeInfoCount();

        void GetTypeInfo();

        void GetIDsOfNames();

        void Invoke();
    }

    [GeneratedComInterface]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    internal partial interface IShellWindows : IDispatch
    {
        void Count();

        void Item();

        void NewEnum();

        void Register();

        void RegisterPending();

        void Revoke();

        void OnNavigate();

        void OnActivated();

        IServiceProvider? FindWindowSW(in Variant location, in Variant root, int type, out int window, int options);
    }

    [GeneratedComInterface]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    internal partial interface IServiceProvider
    {
        void QueryService(in Guid service, in Guid iid, out IShellBrowser browser);
    }

    [GeneratedComInterface]
    [Guid("00000114-0000-0000-C000-000000000046")]
    internal partial interface IOleWindow
    {
        void GetWindow();

        void ContextSensitiveHelp();
    }

    [GeneratedComInterface]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    internal partial interface IShellBrowser : IOleWindow
    {
        void InsertMenusSB();

        void SetMenuSB();

        void RemoveMenusSB();

        void SetStatusTextSB();

        void EnableModelessSB();

        void TranslateAcceleratorSB();

        void BrowseObject();

        void GetViewStateStream();

        void GetControlWindow();

        void SendControlMsg();

        IShellView QueryActiveShellView();
    }

    [GeneratedComInterface]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    internal partial interface IShellView : IOleWindow
    {
        void TranslateAccelerator();

        void EnableModeless();

        void UIActivate();

        void Refresh();

        void CreateViewWindow();

        void DestroyViewWindow();

        void GetCurrentInfo();

        void AddPropertySheetPages();

        void SaveViewState();

        void SelectItem();

        void GetItemObject(uint item, in Guid iid, out IShellFolderViewDual view);
    }

    [GeneratedComInterface]
    [Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
    internal partial interface IShellFolderViewDual : IDispatch
    {
        IShellDispatch2 Application();
    }

    [GeneratedComInterface]
    [Guid("D8F015C0-C278-11CE-A49E-444553540000")]
    internal partial interface IShellDispatch : IDispatch
    {
        void Application();

        void Parent();

        void NameSpace();

        void BrowseForFolder();

        void Windows();

        void Open();

        void Explore();

        void MinimizeAll();

        void UndoMinimizeALL();

        void FileRun();

        void CascadeWindows();

        void TileVertically();

        void TileHorizontally();

        void ShutdownWindows();

        void Suspend();

        void EjectPC();

        void SetTime();

        void TrayProperties();

        void Help();

        void FindFiles();

        void FindComputer();

        void RefreshMenu();

        void ControlPanelItem();
    }

    [GeneratedComInterface]
    [Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
    internal partial interface IShellDispatch2 : IShellDispatch
    {
        void IsRestricted();

        void ShellExecute([MarshalAs(UnmanagedType.BStr)] string file, Variant arguments, Variant folder, Variant verb, Variant show);
    }
}
