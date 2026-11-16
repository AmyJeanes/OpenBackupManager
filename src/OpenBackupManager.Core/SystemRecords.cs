using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace OpenBackupManager.Core;

// What each OS itself keeps about obm's programs, for obm diagnostics export
public static class SystemRecords
{
    private const string RuntimeProvider = ".NET Runtime";

    private static readonly TimeSpan JournalTimeout = TimeSpan.FromSeconds(30);

    // Their crashes, hangs and .NET's errors in Windows' Application log, oldest first. Only those of the programs in
    // folders, such as this user's profile, as the log is the whole PC's and redaction only knows this user's details
    [SupportedOSPlatform("windows")]
    public static string WindowsEvents(IReadOnlyList<string> exes, IReadOnlyList<string> folders, DateTimeOffset since)
    {
        List<WindowsEvent> events = [];
        try
        {
            foreach (var record in CrashEvents.Query("(Provider[@Name='Application Error'] or Provider[@Name='Application Hang'] or Provider[@Name='.NET Runtime'])", since))
            {
                using (record)
                {
                    var values = record.Properties.Select(p => p.Value).ToList();
                    var description = record.FormatDescription() ?? string.Join(Environment.NewLine, values);
                    if (exes.Any(exe => description.Contains(exe, StringComparison.OrdinalIgnoreCase)))
                    {
                        events.Add(new WindowsEvent(
                            record.TimeCreated is { } created ? new DateTimeOffset(created) : since,
                            record.ProviderName,
                            record.Id,
                            record.ProviderName == RuntimeProvider ? record.ProcessId : CrashEvents.ProcessIdIn(record.ProviderName, values),
                            CrashEvents.AppPathIn(record.ProviderName, values),
                            description));
                    }
                }
            }
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            return $"Windows' Application log can't be read: {e.Message}";
        }

        return Describe(events, exes, folders, since);
    }

    // Windows' entries name the program's path. .NET's don't, so they're kept for the processes Windows' entries kept are
    // about. The rest are only counted
    internal static string Describe(IReadOnlyList<WindowsEvent> events, IReadOnlyList<string> exes, IReadOnlyList<string> folders, DateTimeOffset since)
    {
        var own = events.Where(e => e.AppPath is { } path && folders.Any(folder => IsIn(path, folder))).ToList();
        var processes = own.Select(e => e.ProcessId).OfType<int>().ToHashSet();
        var kept = events.Where(e => own.Contains(e) || e.Provider == RuntimeProvider && e.ProcessId is { } id && processes.Contains(id)).ToList();

        var text = new StringBuilder();
        foreach (var e in kept)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{e.Time:yyyy-MM-dd HH:mm:ss zzz}, {e.Provider} {e.Id}")
                .AppendLine(e.Description.TrimEnd())
                .AppendLine();
        }

        if (kept.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"There's nothing from {Either(exes)} since {since:yyyy-MM-dd HH:mm:ss zzz}");
        }

        var left = events.Count - kept.Count;
        if (left > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{left} other {(left == 1 ? "entry was" : "entries were")} left out, as they may be from other accounts");
        }

        return text.ToString();
    }

    // Windows' paths, whatever OS the tests run on
    private static bool IsIn(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\', '/') + '\\', StringComparison.OrdinalIgnoreCase);

    // macOS's crash reports, which it names after the program
    public static IEnumerable<string> MacOSReports(IReadOnlyList<string> exes, DateTimeOffset since) =>
        Directory.Exists(DiagnosticReports.Folder)
            ? exes.SelectMany(exe => new DirectoryInfo(DiagnosticReports.Folder).GetFiles(exe + "-*.ips"))
                .Where(file => file.LastWriteTimeUtc >= since.UtcDateTime)
                .Select(file => file.FullName)
                .Order(StringComparer.Ordinal)
            : [];

    // Everything the service's engines printed, which systemd keeps in the user's journal, with when systemd started
    // and stopped them
    public static string Journal(string unit, DateTimeOffset since) =>
        Core.Journal.Run(JournalTimeout, "--user", "--unit=" + unit, string.Create(CultureInfo.InvariantCulture, $"--since=@{since.ToUnixTimeSeconds()}"), "--output=short-iso", "--no-pager") is { } journal
            ? journal.Output + journal.Errors
            : "journalctl isn't here, or didn't finish in time";

    private static string Either(IReadOnlyList<string> names) =>
        names.Count > 1 ? string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1] : string.Join("", names);
}

// An entry in Windows' Application log, with the process and program it's about where it says
internal sealed record WindowsEvent(DateTimeOffset Time, string Provider, int Id, int? ProcessId, string? AppPath, string Description);
