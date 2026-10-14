using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenBackupManager.Core;

/// <summary>
/// Reports of the app closing unexpectedly, in the state folder's crashes folder. Each is plain English text, so it
/// can be read and shared as it is. A running file in the state folder notes the copy that's running, so the next
/// start can tell when a run ended without quitting or writing a report, as one that crashed outside .NET does
/// </summary>
/// <param name="logPattern">The app's own log files in the logs folder, such as app-*.log</param>
public sealed class CrashReports(AppPaths paths, TimeProvider time, string logPattern)
{
    private const int Kept = 10;
    private const int LogLines = 100;
    private const string ShownFile = "shown";
    private const string RunningFile = "running";

    public string Folder => Path.Combine(paths.State, "crashes");

    // This copy, once it has started
    public Run? Current { get; private set; }

    // Notes this copy as running, with where its log starts, and returns the last run if it didn't end by quitting or
    // with a report. Called before the run logs anything
    public Run? Start(int processId, DateTimeOffset started, string version)
    {
        var file = Path.Combine(paths.State, RunningFile);
        var previous = File.Exists(file) ? Run.Parse(File.ReadAllLines(file)) : null;
        var log = NewestLog();
        Current = new Run(processId, started, version, log?.Name, log?.Length ?? 0);
        Directory.CreateDirectory(paths.State);
        File.WriteAllLines(file, Current.Lines());
        return previous;
    }

    // When the app quits as it should, or has written a report as it crashes
    public void Stopped() => File.Delete(Path.Combine(paths.State, RunningFile));

    // Writes a report on a run that closed unexpectedly, with the end of its own log. closedAt is only needed for a
    // run that closed before now
    public string Write(string app, Run run, string details, DateTimeOffset? closedAt = null)
    {
        var when = closedAt ?? time.GetLocalNow();
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"{app} closed unexpectedly at {when:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Version: {run.Version}")
            .AppendLine(CultureInfo.InvariantCulture, $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")
            .AppendLine(CultureInfo.InvariantCulture, $".NET: {RuntimeInformation.FrameworkDescription}")
            .AppendLine()
            .AppendLine(details)
            .AppendLine()
            .AppendLine("Recent log:")
            .AppendLine(RecentLog(run));

        Directory.CreateDirectory(Folder);
        // Named by when they're written, so they sort oldest first
        var file = Path.Combine(Folder, $"crash-{time.GetLocalNow():yyyyMMdd-HHmmss-fff}.txt");
        File.WriteAllText(file, report.ToString());
        foreach (var old in Reports().SkipLast(Kept))
        {
            File.Delete(old);
        }

        return file;
    }

    // The newest report written since this was last asked, so each crash is shown once
    public string? TakeNew()
    {
        var shownFile = Path.Combine(Folder, ShownFile);
        var shown = File.Exists(shownFile) ? File.ReadAllText(shownFile).Trim() : "";
        var newest = Reports().LastOrDefault();
        if (newest is null || string.CompareOrdinal(Path.GetFileName(newest), shown) <= 0)
        {
            return null;
        }

        File.WriteAllText(shownFile, Path.GetFileName(newest));
        return File.ReadAllText(newest);
    }

    private IEnumerable<string> Reports() =>
        Directory.Exists(Folder) ? Directory.GetFiles(Folder, "crash-*.txt").Order(StringComparer.Ordinal) : [];

    // The one written to last, since a day's file can carry on in another once it's 10 MB
    private FileInfo? NewestLog() =>
        Directory.Exists(paths.Logs) ? new DirectoryInfo(paths.Logs).GetFiles(logPattern).MaxBy(f => f.LastWriteTimeUtc) : null;

    // The run's last lines, from where its log started, or the whole newest file if the run started another. Read while
    // the app may still be writing it
    private string RecentLog(Run run)
    {
        var log = NewestLog();
        if (log is null)
        {
            return "(none)";
        }

        using var stream = new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (log.Name == run.LogFile && run.LogLength <= stream.Length)
        {
            stream.Position = run.LogLength;
        }

        using var reader = new StreamReader(stream);
        var lines = new Queue<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Enqueue(line);
            if (lines.Count > LogLines)
            {
                lines.Dequeue();
            }
        }

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "(none)";
    }
}

// A run of the app, and where its log started: the newest log file and how long it was then
public sealed record Run(int ProcessId, DateTimeOffset Started, string Version, string? LogFile, long LogLength)
{
    internal IEnumerable<string> Lines() =>
    [
        ProcessId.ToString(CultureInfo.InvariantCulture),
        Started.ToString("O", CultureInfo.InvariantCulture),
        Version,
        LogFile ?? "",
        LogLength.ToString(CultureInfo.InvariantCulture),
    ];

    // Null if the file isn't one the app wrote
    internal static Run? Parse(string[] lines) =>
        lines.Length == 5
        && int.TryParse(lines[0], CultureInfo.InvariantCulture, out var processId)
        && DateTimeOffset.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
        && long.TryParse(lines[4], CultureInfo.InvariantCulture, out var logLength)
            ? new Run(processId, started, lines[2], lines[3] is { Length: > 0 } logFile ? logFile : null, logLength)
            : null;
}
