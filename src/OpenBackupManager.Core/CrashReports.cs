using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenBackupManager.Core;

/// <summary>
/// Reports of the app or the engine closing unexpectedly, in the state folder's crashes folder, where the app shows
/// both. Each is plain English text, so it can be read and shared as it is. A running file in the state folder notes
/// the copy that's running, so the next start can tell when a run ended without stopping or writing a report, as one
/// that crashed outside .NET does
/// </summary>
/// <param name="name">Which program's: app or engine, which its log files and running file are named after</param>
public sealed class CrashReports(AppPaths paths, TimeProvider time, string name)
{
    public const string App = "app";
    public const string Engine = "engine";

    private const int Kept = 10;
    private const int LogLines = 100;
    private const int PrintedLength = 64 * 1024;
    private const string ShownFile = "shown";

    private static readonly TimeSpan Repeat = TimeSpan.FromMinutes(1);

    private string RunningFile => Path.Combine(paths.State, name + ".running");

    public string Folder => Path.Combine(paths.State, "crashes");

    // This copy, once it has started
    public Run? Current { get; private set; }

    // Notes this copy as running, with where its log starts, and returns the last run if it didn't end by quitting or
    // with a report. Called before the run logs anything
    public Run? Start(int processId, DateTimeOffset started, string version)
    {
        var previous = File.Exists(RunningFile) ? Run.Parse(File.ReadAllLines(RunningFile)) : null;
        var log = NewestLog();
        Current = new Run(processId, started, version, log?.Name, log?.Length ?? 0, BootId.Current());
        Directory.CreateDirectory(paths.State);
        File.WriteAllLines(RunningFile, Current.Lines());
        return previous;
    }

    // When it stops as it should, or has written a report as it crashes
    public void Stopped() => File.Delete(RunningFile);

    // Writes a report on a run that closed unexpectedly, with the end of its own log. closedAt is only needed for a
    // run that closed before now
    public string Write(string program, Run run, string details, DateTimeOffset? closedAt = null) =>
        Write($"{program} closed unexpectedly at {(closedAt ?? time.GetLocalNow()):yyyy-MM-dd HH:mm:ss zzz}", run, details);

    // The report a run repeats, if it closed within a minute of starting, soon after it, as one that can't get going does
    // when it's started again and again. That first report says why, and this keeps the others from replacing older ones
    public string? RepeatOf(Run run)
    {
        if (time.GetUtcNow() - run.Started >= Repeat || Reports().LastOrDefault(IsOwn) is not { } newest)
        {
            return null;
        }

        var name = Path.GetFileName(newest);
        var written = DateTime.ParseExact(name["crash-".Length..][..19], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        return written >= TimeZoneInfo.ConvertTime(run.Started - Repeat, time.LocalTimeZone).DateTime ? name : null;
    }

    // For a run that didn't stop, from what Windows recorded, which catches crashes .NET never sees. Null if there's
    // nothing, and always on macOS and Linux. Throws EventLogException or UnauthorizedAccessException if the event log
    // can't be read, as over SSH
    public string? WriteRecorded(string program, Run run) =>
        OperatingSystem.IsWindows() && CrashEvents.Find(Path.GetFileName(Environment.ProcessPath!), run.ProcessId, run.Started) is { } recorded
            ? Write(program, run, "Windows recorded this:" + Environment.NewLine + recorded.Text, recorded.Time)
            : null;

    // For a run on macOS or Linux that ended without stopping, with what the OS kept: macOS's own crash report, and
    // what it printed as it ended, which is the only place a FailFast's message goes
    public string WriteEnded(string program, Run run)
    {
        var exe = Path.GetFileName(Environment.ProcessPath!);
        var recorded = OperatingSystem.IsMacOS() ? DiagnosticReports.Find(exe, run) : null;
        var printed = OperatingSystem.IsLinux() ? Journal.Printed(run)
            : OperatingSystem.IsMacOS() && name == Engine ? Printed(ErrorOutput(paths), run)
            : null;
        return WriteEnded(program, run, recorded, printed);
    }

    internal string WriteEnded(string program, Run run, CrashEvent? recorded, string? printed)
    {
        var details = new StringBuilder();
        if (recorded is null && printed is null)
        {
            details.Append("It didn't say why. It may have crashed, or been closed by the system or another program.");
            if (WhereToLook(run) is { } where)
            {
                details.AppendLine().AppendLine().Append(where);
            }
        }

        if (recorded is not null)
        {
            details.AppendLine("macOS recorded this:").Append(recorded.Text);
        }

        if (printed is not null)
        {
            details.Append(recorded is null ? "" : Environment.NewLine + Environment.NewLine)
                .AppendLine("What it printed as it closed:")
                .Append(printed);
        }

        string heading;
        if (recorded is not null)
        {
            heading = $"{program} closed unexpectedly at {TimeZoneInfo.ConvertTime(recorded.Time, time.LocalTimeZone):yyyy-MM-dd HH:mm:ss zzz}";
        }
        else if (NewestLog() is { } log && log.LastWriteTimeUtc >= run.Started.UtcDateTime)
        {
            var written = TimeZoneInfo.ConvertTime(new DateTimeOffset(log.LastWriteTimeUtc), time.LocalTimeZone);
            heading = $"{program} closed unexpectedly after {written:yyyy-MM-dd HH:mm:ss zzz}, when it last wrote to its log";
        }
        else
        {
            heading = $"{program} closed unexpectedly some time after it started at {TimeZoneInfo.ConvertTime(run.Started, time.LocalTimeZone):yyyy-MM-dd HH:mm:ss zzz}";
        }

        return Write(heading, run, details.ToString());
    }

    // Where launchd sends the engine's stderr when it runs as a service. Not named like its logs, which are found by name
    public static string ErrorOutput(AppPaths paths) => Path.Combine(paths.Logs, "obmd-stderr.log");

    // From .NET's last words, as a FailFast prints them, to the end
    internal static string? Ending(string output)
    {
        var start = output.LastIndexOf("Process terminated.", StringComparison.Ordinal);
        return start < 0 ? null : output[start..].TrimEnd();
    }

    // The newest report written since this was last asked, so each crash is shown once
    public CrashReport? TakeNew()
    {
        var shownFile = Path.Combine(Folder, ShownFile);
        var shown = File.Exists(shownFile) ? File.ReadAllText(shownFile).Trim() : "";
        var newest = Reports().LastOrDefault();
        if (newest is null || string.CompareOrdinal(Path.GetFileName(newest), shown) <= 0)
        {
            return null;
        }

        File.WriteAllText(shownFile, Path.GetFileName(newest));
        return new CrashReport(Path.GetFileNameWithoutExtension(newest).EndsWith("-" + Engine, StringComparison.Ordinal) ? Engine : App, File.ReadAllText(newest));
    }

    private string Write(string heading, Run run, string details)
    {
        var report = new StringBuilder()
            .AppendLine(heading)
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
        var file = Path.Combine(Folder, $"crash-{time.GetLocalNow():yyyyMMdd-HHmmss-fff}-{name}.txt");
        File.WriteAllText(file, report.ToString());
        foreach (var old in Reports().SkipLast(Kept))
        {
            File.Delete(old);
        }

        return file;
    }

    private bool IsOwn(string report) => Path.GetFileNameWithoutExtension(report).EndsWith("-" + name, StringComparison.Ordinal);

    // The end of a file it printed to, if it printed to it during the run. It only grows, so only the end is read
    private static string? Printed(string file, Run run)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.LastWriteTimeUtc < run.Started.UtcDateTime)
        {
            return null;
        }

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = Math.Max(0, stream.Length - PrintedLength);
        using var reader = new StreamReader(stream);
        return Ending(reader.ReadToEnd());
    }

    private static string? WhereToLook(Run run) =>
        OperatingSystem.IsMacOS() ? "If it ran as a service, /var/log/com.apple.xpc.launchd/launchd.log may say what closed it."
        : KeepsCoreDumps() ? string.Create(CultureInfo.InvariantCulture, $"If it crashed, coredumpctl info {run.ProcessId} may say more.")
        : null;

    // Whether systemd-coredump is what the kernel hands crashes to. Ubuntu's Apport, its usual one, ignores programs that
    // weren't installed from a package, so it has nothing to look in
    private static bool KeepsCoreDumps()
    {
        try
        {
            return File.ReadAllText("/proc/sys/kernel/core_pattern").Contains("systemd-coredump", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private IEnumerable<string> Reports() =>
        Directory.Exists(Folder) ? Directory.GetFiles(Folder, "crash-*.txt").Order(StringComparer.Ordinal) : [];

    // The one written to last, since a day's file can carry on in another once it's 10 MB
    private FileInfo? NewestLog() =>
        Directory.Exists(paths.Logs) ? new DirectoryInfo(paths.Logs).GetFiles(name + "-*.log").MaxBy(f => f.LastWriteTimeUtc) : null;

    // The run's last lines, from where its log started, or the whole newest file if the run started another. Read while
    // it may still be writing it
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

// The report, and which program's it is: app or engine
public sealed record CrashReport(string Name, string Text);

// A run of the app or the engine, and where its log started: the newest log file and how long it was then. Boot is the
// computer's boot ID on macOS and Linux, and null on Windows or from a version that didn't note it
public sealed record Run(int ProcessId, DateTimeOffset Started, string Version, string? LogFile, long LogLength, string? Boot = null)
{
    internal IEnumerable<string> Lines() =>
    [
        ProcessId.ToString(CultureInfo.InvariantCulture),
        Started.ToString("O", CultureInfo.InvariantCulture),
        Version,
        LogFile ?? "",
        LogLength.ToString(CultureInfo.InvariantCulture),
        Boot ?? "",
    ];

    // Null if the file isn't one it wrote
    internal static Run? Parse(string[] lines) =>
        lines.Length is 5 or 6
        && int.TryParse(lines[0], CultureInfo.InvariantCulture, out var processId)
        && DateTimeOffset.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
        && long.TryParse(lines[4], CultureInfo.InvariantCulture, out var logLength)
            ? new Run(processId, started, lines[2], NullIfEmpty(lines[3]), logLength, lines.Length == 6 ? NullIfEmpty(lines[5]) : null)
            : null;

    private static string? NullIfEmpty(string value) => value.Length > 0 ? value : null;
}
