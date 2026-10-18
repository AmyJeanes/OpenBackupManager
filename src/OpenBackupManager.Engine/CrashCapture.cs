using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;

namespace OpenBackupManager.Engine;

// Writes a crash report when an error nothing caught is about to end the engine, as the tray app's CrashCapture does
// for the app, into the same crashes folder, where the app shows it. Crashes .NET never sees are found at the next
// start from what Windows recorded
public sealed partial class CrashCapture(CrashReports reports, ILogger<CrashCapture> logger)
{
    private const string Program = "obmd";

    private int _captured;

    // Before anything else logs, so a report about the last run ends with its log
    public void Start()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Capture((Exception)e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => LogUnobserved(e.Exception);

        Run? previous;
        using (var process = Process.GetCurrentProcess())
        {
            previous = reports.Start(process.Id, process.StartTime, AppVersion.Current);
        }

        if (previous is null)
        {
            return;
        }

        bool recorded;
        try
        {
            recorded = reports.WriteRecorded(Program, previous);
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            LogCantReadEvents(e);
            return;
        }

        if (recorded)
        {
            LogFoundCrash();
        }
        else
        {
            LogEndedWithoutStopping();
        }
    }

    // Once it has stopped as it should, or found it can't run
    public void Stopped() => reports.Stopped();

    private void Capture(Exception error)
    {
        if (Interlocked.Exchange(ref _captured, 1) == 1)
        {
            return;
        }

        // Each on its own, so the report is still written if logging fails, and nothing here replaces the error that's
        // ending the engine
#pragma warning disable CA1031
        try
        {
            LogCrash(error);
        }
        catch (Exception)
        {
        }

        try
        {
            reports.Write(Program, reports.Current ?? new Run(Environment.ProcessId, DateTimeOffset.Now, AppVersion.Current, null, 0), error.ToString());
            reports.Stopped();
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Stopping because of an error nothing caught")]
    private partial void LogCrash(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run stopped unexpectedly, and Windows recorded why, so it's in a crash report")]
    private partial void LogFoundCrash();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run ended without stopping, such as when it was killed, the power went or, on macOS and Linux, it crashed outside .NET")]
    private partial void LogEndedWithoutStopping();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run ended without stopping, and Windows' event log couldn't be read to see why")]
    private partial void LogCantReadEvents(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A background task failed and nothing waited for it")]
    private partial void LogUnobserved(Exception error);
}
