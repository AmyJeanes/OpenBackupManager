using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows;

// Writes a crash report when an error nothing caught is about to close the app. The error stays unhandled, so Windows
// still sees the crash and can restart the app. Crashes .NET never sees, such as one inside Windows' own code, are
// found at the next start from what Windows recorded
public sealed partial class CrashCapture(CrashReports reports, AppInfo app, ILogger<CrashCapture> logger)
{
    // With the commit it was built from, which AppInfo leaves out
    private static readonly string Version =
        typeof(CrashCapture).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private int _captured;

    // Before anything else logs, so a report about the last run ends with its log
    public void Start()
    {
        Run? previous;
        using (var process = Process.GetCurrentProcess())
        {
            previous = reports.Start(process.Id, process.StartTime, Version);
        }

        if (previous is null)
        {
            return;
        }

        bool recorded;
        try
        {
            recorded = reports.WriteRecorded(app.Name, previous);
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
            // As when the app was ended from Task Manager or the power went, which aren't crashes
            LogEndedWithoutQuitting();
        }
    }

    public void Capture(Exception error)
    {
        // WinUI and .NET can both report the same error
        if (Interlocked.Exchange(ref _captured, 1) == 1)
        {
            return;
        }

        // Each on its own, so the report is still written if logging fails, and nothing here replaces the error that's
        // closing the app
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
            // Its run is only unknown if it crashed while starting, before its log started
            reports.Write(app.Name, reports.Current ?? new Run(Environment.ProcessId, DateTimeOffset.Now, Version, null, 0), error.ToString());
            reports.Stopped();
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    // A task's error that nothing waited for, which doesn't close the app but would otherwise go unseen
    public void Unobserved(Exception error) => LogUnobserved(error);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Closing because of an error nothing caught")]
    private partial void LogCrash(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run closed unexpectedly, and Windows recorded why, so it's in a crash report")]
    private partial void LogFoundCrash();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run ended without quitting, such as when it was ended from Task Manager or the power went")]
    private partial void LogEndedWithoutQuitting();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The last run ended without quitting, and Windows' event log couldn't be read to see why")]
    private partial void LogCantReadEvents(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A background task failed and nothing waited for it")]
    private partial void LogUnobserved(Exception error);
}
