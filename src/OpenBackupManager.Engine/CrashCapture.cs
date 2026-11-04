using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;

namespace OpenBackupManager.Engine;

// Writes a crash report when an error nothing caught is about to end the engine, as the tray app's CrashCapture does
// for the app, into the same crashes folder, where the app shows it. Crashes .NET never sees are found at the next
// start, from what Windows recorded in its event log, or on macOS and Linux from the running file the run left
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

        if (!OperatingSystem.IsWindows())
        {
            Ended(previous);
            return;
        }

        if (reports.RepeatOf(previous) is { } earlier)
        {
            LogAgain(earlier);
            return;
        }

        string? recorded;
        try
        {
            recorded = reports.WriteRecorded(Program, previous);
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            LogCantReadEvents(e);
            return;
        }

        if (recorded is not null)
        {
            LogFoundCrash(Path.GetFileName(recorded));
        }
        else
        {
            LogEndedWithoutSayingWhy();
        }
    }

    // Once it has stopped as it should, or found it can't run
    public void Stopped() => reports.Stopped();

    // On macOS and Linux, a run still going when the computer restarted or lost power isn't a crash, and only the boot ID
    // tells them apart
    private void Ended(Run previous)
    {
        if (previous.Boot is null || reports.Current!.Boot is null)
        {
            LogCantTellFromARestart();
        }
        else if (previous.Boot != reports.Current.Boot)
        {
            LogRestartedSince();
        }
        else if (reports.RepeatOf(previous) is { } earlier)
        {
            LogAgain(earlier);
        }
        else
        {
            LogWroteReport(Path.GetFileName(reports.WriteEnded(Program, previous)));
        }
    }

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
            var run = reports.Current ?? new Run(Environment.ProcessId, DateTimeOffset.Now, AppVersion.Current, null, 0);
            if (reports.RepeatOf(run) is null)
            {
                reports.Write(Program, run, error.ToString());
            }

            reports.Stopped();
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Closing because of an error nothing caught")]
    private partial void LogCrash(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly. Its crash report, from what Windows recorded, is {Report}")]
    private partial void LogFoundCrash(string report);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly. Its crash report is {Report}")]
    private partial void LogWroteReport(string report);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly without saying why, such as when Task Manager ended it or the power went")]
    private partial void LogEndedWithoutSayingWhy();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly, but it can't be told from a restart, so it's not counted as a crash")]
    private partial void LogCantTellFromARestart();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly, but the computer has restarted since, so it's not counted as a crash")]
    private partial void LogRestartedSince();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly again soon after starting, as it did in {Report}")]
    private partial void LogAgain(string report);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly, and Windows' event log couldn't be read to see why")]
    private partial void LogCantReadEvents(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A background task failed and nothing waited for it")]
    private partial void LogUnobserved(Exception error);
}
