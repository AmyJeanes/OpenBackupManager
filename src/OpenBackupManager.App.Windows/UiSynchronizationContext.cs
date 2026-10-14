using Microsoft.UI.Dispatching;

namespace OpenBackupManager.App.Windows;

// The UI thread's synchronization context, which async code resumes on and the app queues its own UI work through.
// DispatcherQueue ends the app as soon as an error leaves one of its callbacks, without WinUI or .NET reporting it, so
// this reports it first
internal sealed class UiSynchronizationContext(DispatcherQueue queue, CrashCapture crashes) : SynchronizationContext
{
    // The one place work is queued directly, which BannedSymbols.txt stops anywhere else
#pragma warning disable RS0030
    public override void Post(SendOrPostCallback callback, object? state) => queue.TryEnqueue(() =>
#pragma warning restore RS0030
    {
        try
        {
            callback(state);
        }
        // Reported without being caught, so the app still closes with the error as it was thrown
        catch (Exception e) when (Report(e))
        {
        }
    });

    // As DispatcherQueue's own context does, since waiting on the UI thread for itself would never finish
    public override void Send(SendOrPostCallback callback, object? state) => throw new NotSupportedException();

    public override SynchronizationContext CreateCopy() => new UiSynchronizationContext(queue, crashes);

    private bool Report(Exception error)
    {
        crashes.Capture(error);
        return false;
    }
}
