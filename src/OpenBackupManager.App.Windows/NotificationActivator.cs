using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OpenBackupManager.App.Windows;

// The COM class Windows calls when a notification or one of its buttons is clicked. While the app runs it's registered
// with COM, and when it isn't, Windows starts the app from the command in the registry and calls it once registered
internal static partial class NotificationActivator
{
    private const uint ClassContextLocalServer = 4;
    private const uint MultipleUse = 1;
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    // Called with the clicked notification or button's arguments
    public static uint Register(Guid clsid, Action<string> activated)
    {
        var factory = ComWrappers.GetOrCreateComInterfaceForObject(new Factory(new Callback(activated)), CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(clsid, factory, ClassContextLocalServer, MultipleUse, out var cookie));
            return cookie;
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    public static void Revoke(uint cookie) => _ = CoRevokeClassObject(cookie);

    [LibraryImport("ole32.dll")]
    private static partial int CoRegisterClassObject(in Guid clsid, nint unknown, uint classContext, uint flags, out uint cookie);

    [LibraryImport("ole32.dll")]
    private static partial int CoRevokeClassObject(uint cookie);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
    internal partial interface INotificationActivationCallback
    {
        void Activate(string appUserModelId, string? invokedArgs, nint data, uint count);
    }

    [GeneratedComInterface]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal partial interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(nint outer, in Guid iid, out nint instance);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockServer);
    }

    [GeneratedComClass]
    internal sealed partial class Callback(Action<string> activated) : INotificationActivationCallback
    {
        public void Activate(string appUserModelId, string? invokedArgs, nint data, uint count) => activated(invokedArgs ?? "");
    }

    [GeneratedComClass]
    internal sealed partial class Factory(Callback callback) : IClassFactory
    {
        private const int NoAggregation = unchecked((int)0x80040110);

        public int CreateInstance(nint outer, in Guid iid, out nint instance)
        {
            instance = 0;
            if (outer != 0)
            {
                return NoAggregation;
            }

            var unknown = ComWrappers.GetOrCreateComInterfaceForObject(callback, CreateComInterfaceFlags.None);
            try
            {
                return Marshal.QueryInterface(unknown, iid, out instance);
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        public int LockServer(bool lockServer) => 0;
    }
}
