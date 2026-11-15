using Microsoft.UI.Dispatching;

namespace OpenBackupManager.App.Windows;

// The UI thread's synchronization context, which async code resumes on and the app queues its own UI work through.
// DispatcherQueue ends the app as soon as an error leaves one of its callbacks, without WinUI or .NET reporting it, so
// this reports it first (https://github.com/microsoft/microsoft-ui-xaml/issues/8940)
internal sealed class UiSynchronizationContext(DispatcherQueue queue, CrashCapture crashes) : SynchronizationContext
{
    private static bool _stopped;

    // The one place work is queued directly, which BannedSymbols.txt stops anywhere else
#pragma warning disable RS0030
    public override void Post(SendOrPostCallback callback, object? state) => queue.TryEnqueue(() =>
#pragma warning restore RS0030
    {
        if (_stopped)
        {
            return;
        }

        try
        {
            callback(state);
        }
        // Reported without being caught, so the app still closes with the error as it was thrown
        catch (Exception e) when (Report(e))
        {
        }
    });

    // As the app exits. Work queued after that is dropped, as WinUI throws from the windows it's taking down
    public static void Stop() => _stopped = true;

    // As DispatcherQueue's own context does, since waiting on the UI thread for itself would never finish
    public override void Send(SendOrPostCallback callback, object? state) => throw new NotSupportedException();

    public override SynchronizationContext CreateCopy() => new UiSynchronizationContext(queue, crashes);

    private bool Report(Exception error)
    {
        crashes.Capture(error);
        return false;
    }
}
