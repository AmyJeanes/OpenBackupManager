using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Notifications;

namespace OpenBackupManager.Platform.Windows;

// The COM class Windows calls when a notification or one of its buttons is clicked. While the app runs it's registered
// with COM, and when it isn't, Windows starts the app from the command in the registry and calls it once registered
internal static partial class NotificationActivator
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    // Called with the clicked notification or button's arguments
    public static uint Register(Guid clsid, Action<string> activated)
    {
        PInvoke.CoRegisterClassObject(clsid, new Factory(new Callback(activated)), CLSCTX.CLSCTX_LOCAL_SERVER, REGCLS.REGCLS_MULTIPLEUSE, out var cookie)
            .ThrowOnFailure();
        return cookie;
    }

    public static void Revoke(uint cookie) => _ = PInvoke.CoRevokeClassObject(cookie);

    [GeneratedComClass]
    internal sealed partial class Callback(Action<string> activated) : INotificationActivationCallback
    {
        public unsafe void Activate(PCWSTR appUserModelId, PCWSTR invokedArgs, NOTIFICATION_USER_INPUT_DATA* data, uint count) =>
            activated(invokedArgs.ToString() ?? "");
    }

    [GeneratedComClass]
    internal sealed partial class Factory(Callback callback) : IClassFactory
    {
        public unsafe void CreateInstance(object? outer, Guid* iid, out void* instance)
        {
            if (outer is not null)
            {
                HRESULT.CLASS_E_NOAGGREGATION.ThrowOnFailure();
            }

            var unknown = ComWrappers.GetOrCreateComInterfaceForObject(callback, CreateComInterfaceFlags.None);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, *iid, out var result));
                instance = (void*)result;
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        public void LockServer(BOOL lockServer)
        {
        }
    }
}
