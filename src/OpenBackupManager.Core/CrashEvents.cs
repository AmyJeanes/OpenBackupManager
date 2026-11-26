using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;

namespace OpenBackupManager.Core;

// What Windows and .NET recorded in the Application event log when a process crashed or was ended while it wasn't
// responding. They record crashes a program can't see itself, such as one inside Windows' own code
internal static class CrashEvents
{
    internal const string ApplicationError = "Application Error";
    internal const string ApplicationHang = "Application Hang";

    // Windows reuses process IDs, so only events from after the process started count. Windows' own entries can be
    // turned off, as they are on GitHub's runners, so .NET's entry will do on its own
    [SupportedOSPlatform("windows")]
    public static CrashEvent? Find(string exe, int processId, DateTimeOffset started)
    {
        var windows = WindowsEntry(exe, processId, started);
        var runtime = RuntimeEntry(exe, processId, started);
        return windows is not null && runtime is not null
            ? windows with { Text = windows.Text + Environment.NewLine + Environment.NewLine + runtime.Text }
            : windows ?? runtime;
    }

    // Application Error for a crash, or Application Hang for a process ended while it wasn't responding. Windows Error
    // Reporting writes them, so the process is named in their data
    [SupportedOSPlatform("windows")]
    private static CrashEvent? WindowsEntry(string exe, int processId, DateTimeOffset started)
    {
        foreach (var record in Query(
            "((Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002))",
            started))
        {
            using (record)
            {
                var values = record.Properties.Select(p => p.Value).ToList();
                if (IsOf(record.ProviderName, values, exe, processId))
                {
                    return Event(record, started, record.FormatDescription() ?? string.Join(Environment.NewLine, values));
                }
            }
        }

        return null;
    }

    // Why .NET ended the process, with the stack, as for a stack overflow or FailFast. The crashing process writes it
    // itself, so it's the event's own process. Windows can give the ID to another process once the app ends, so the
    // exe must be named in it too, which it is whatever language the rest is in
    [SupportedOSPlatform("windows")]
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

    [SupportedOSPlatform("windows")]
    private static CrashEvent Event(EventRecord record, DateTimeOffset started, string text) =>
        new(record.TimeCreated is { } created ? new DateTimeOffset(created) : started, text);

    [SupportedOSPlatform("windows")]
    public static IEnumerable<EventRecord> Query(string condition, DateTimeOffset since)
    {
        var time = since.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, $"*[System[{condition} and TimeCreated[@SystemTime>='{time}']]]"));
        while (reader.ReadEvent() is { } record)
        {
            yield return record;
        }
    }

    // Whether an Application Error or Application Hang entry, from its data, is that process's
    internal static bool IsOf(string? provider, IReadOnlyList<object?> values, string exe, int processId) =>
        values.Any(v => v is string name && name.Equals(exe, StringComparison.OrdinalIgnoreCase)) && ProcessIdIn(provider, values) == processId;

    // From its place in Application Error's or Application Hang's data, as other values look like IDs too. A number in
    // Windows 11's entries, and hex in Windows 10's, without the 0x their message adds
    internal static int? ProcessIdIn(string? provider, IReadOnlyList<object?> values) =>
        ValueIn(provider, values, error: 8, hang: 2) switch
        {
            int id => id,
            uint id => (int)id,
            string text when int.TryParse(
                text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.AsSpan(2) : text,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var id) => id,
            _ => null,
        };

    // The program's full path, from its place in Application Error's or Application Hang's data
    internal static string? AppPathIn(string? provider, IReadOnlyList<object?> values) =>
        ValueIn(provider, values, error: 10, hang: 5) as string;

    private static object? ValueIn(string? provider, IReadOnlyList<object?> values, int error, int hang)
    {
        var at = provider switch
        {
            ApplicationError => error,
            ApplicationHang => hang,
            _ => -1,
        };
        return at >= 0 && at < values.Count ? values[at] : null;
    }
}

internal sealed record CrashEvent(DateTimeOffset Time, string Text);
