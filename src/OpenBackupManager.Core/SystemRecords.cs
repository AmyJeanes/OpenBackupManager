using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace OpenBackupManager.Core;

// What each OS itself keeps about obm's programs, for obm diagnostics export
public static class SystemRecords
{
    private static readonly TimeSpan JournalTimeout = TimeSpan.FromSeconds(30);

    // Their crashes, hangs and .NET's errors in Windows' Application log, oldest first
    [SupportedOSPlatform("windows")]
    public static string WindowsEvents(IReadOnlyList<string> exes, DateTimeOffset since)
    {
        var text = new StringBuilder();
        try
        {
            foreach (var record in CrashEvents.Query("(Provider[@Name='Application Error'] or Provider[@Name='Application Hang'] or Provider[@Name='.NET Runtime'])", since))
            {
                using (record)
                {
                    var description = record.FormatDescription() ?? string.Join(Environment.NewLine, record.Properties.Select(p => p.Value));
                    if (exes.Any(exe => description.Contains(exe, StringComparison.OrdinalIgnoreCase)))
                    {
                        var time = record.TimeCreated is { } created ? new DateTimeOffset(created) : since;
                        text.AppendLine(CultureInfo.InvariantCulture, $"{time:yyyy-MM-dd HH:mm:ss zzz}, {record.ProviderName} {record.Id}")
                            .AppendLine(description.TrimEnd())
                            .AppendLine();
                    }
                }
            }
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            return $"Windows' Application log can't be read: {e.Message}";
        }

        return text.Length > 0 ? text.ToString() : $"There's nothing from {Either(exes)} since {since:yyyy-MM-dd HH:mm:ss zzz}";
    }

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
