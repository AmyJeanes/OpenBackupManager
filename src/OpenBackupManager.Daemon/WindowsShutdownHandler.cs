using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace OpenBackupManager.Daemon;

// Stops the host when Windows is about to end the process: its console closing, or the user signing out or shutting
// down. Windows ends it as soon as each of these has been handled, so this waits for the host to stop first. The host
// itself only handles Ctrl+C and Ctrl+Break
[SupportedOSPlatform("windows")]
internal sealed class WindowsShutdownHandler : IDisposable
{
    private const int CtrlCloseEvent = 2;

    // Windows' limit when the console closes. Signing out waits longer, but shows its screen of apps preventing it
    private static readonly TimeSpan Allowed = TimeSpan.FromSeconds(5);

    private readonly IHostApplicationLifetime _lifetime;
    private readonly TaskCompletionSource _stopped = new();
    private readonly NativeMethods.HandlerRoutine _consoleHandler;

    public WindowsShutdownHandler(IHost host)
    {
        _lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        _consoleHandler = OnConsoleEvent;
        NativeMethods.SetConsoleCtrlHandler(_consoleHandler, true);
        // .NET loads user32.dll, so Windows tells the process about signing out and shutting down through a window
        // rather than the console. Subscribing makes SystemEvents create a hidden window, on a thread of its own, to
        // receive it
        SystemEvents.SessionEnded += OnSessionEnded;
    }

    // Once the host has stopped
    public void Dispose()
    {
        _stopped.TrySetResult();
        SystemEvents.SessionEnded -= OnSessionEnded;
        NativeMethods.SetConsoleCtrlHandler(_consoleHandler, false);
    }

    private bool OnConsoleEvent(int type)
    {
        if (type != CtrlCloseEvent)
        {
            return false;
        }

        StopAndWait();
        return true;
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e) => StopAndWait();

    private void StopAndWait()
    {
        _lifetime.StopApplication();
        _stopped.Task.Wait(Allowed);
    }

    private static class NativeMethods
    {
        public delegate bool HandlerRoutine(int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    }
}
