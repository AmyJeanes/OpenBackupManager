using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;

namespace OpenBackupManager.Core;

// What Windows and .NET recorded in the Application event log when a process crashed or was ended while it wasn't
// responding. They record crashes a program can't see itself, such as one inside Windows' own code
[SupportedOSPlatform("windows")]
internal static class CrashEvents
{
    // Windows reuses process IDs, so only events from after the process started count. Windows' own entries can be
    // turned off, as they are on GitHub's runners, so .NET's entry will do on its own
    public static CrashEvent? Find(string exe, int processId, DateTimeOffset started)
    {
        var windows = WindowsEntry(exe, processId, started);
        var runtime = RuntimeEntry(exe, processId, started);
        return windows is not null && runtime is not null
            ? windows with { Text = windows.Text + Environment.NewLine + Environment.NewLine + runtime.Text }
            : windows ?? runtime;
    }

    // Application Error for a crash, or Application Hang for a process ended while it wasn't responding. Windows Error
    // Reporting writes them, so the process is named in their data, as a number though their message shows it in hex
    private static CrashEvent? WindowsEntry(string exe, int processId, DateTimeOffset started)
    {
        foreach (var record in Query(
            "((Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002))",
            started))
        {
            using (record)
            {
                var values = record.Properties.Select(p => p.Value).ToList();
                if (values.Any(v => v is string name && name.Equals(exe, StringComparison.OrdinalIgnoreCase)) && values.Any(v => IsProcessId(v, processId)))
                {
                    return Event(record, started, record.FormatDescription() ?? string.Join(Environment.NewLine, values));
                }
            }
        }

        return null;
    }

    // Why .NET ended the process, with the stack, as for a stack overflow or FailFast. The crashing process writes it
    // itself, so it's the event's own process. Windows can give the ID to another process once the app has ended, so the
    // exe must be named in it too, which it is whatever language the rest is in
    private static CrashEvent? RuntimeEntry(string exe, int processId, DateTimeOffset started)
    {
        foreach (var record in Query("Provider[@Name='.NET Runtime'] and (EventID=1025 or EventID=1026)", started))
        {
            using (record)
            {
                if (record.ProcessId == processId
                    && record.Properties.FirstOrDefault()?.Value is string text
                    && text.Contains(exe, StringComparison.OrdinalIgnoreCase))
                {
                    return Event(record, started, text);
                }
            }
        }

        return null;
    }

    private static CrashEvent Event(EventRecord record, DateTimeOffset started, string text) =>
        new(record.TimeCreated is { } created ? new DateTimeOffset(created) : started, text);

    private static IEnumerable<EventRecord> Query(string condition, DateTimeOffset since)
    {
        var time = since.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, $"*[System[{condition} and TimeCreated[@SystemTime>='{time}']]]"));
        while (reader.ReadEvent() is { } record)
        {
            yield return record;
        }
    }

    private static bool IsProcessId(object? value, int processId) => value switch
    {
        uint id => id == processId,
        int id => id == processId,
        string text => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
            && id == processId,
        _ => false,
    };
}

internal sealed record CrashEvent(DateTimeOffset Time, string Text);
