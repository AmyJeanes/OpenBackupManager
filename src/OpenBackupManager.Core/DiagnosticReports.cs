using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenBackupManager.Core;

// The crash reports macOS writes to ~/Library/Logs/DiagnosticReports, for every crash signal but SIGKILL, whether .NET
// saw the crash or not. Each .ips file is a line of JSON about the file, then the report as JSON
internal static class DiagnosticReports
{
    private const int Frames = 15;

    // ReportCrash writes the file a moment after the process has gone, by when launchd may have started it again
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    public static CrashEvent? Find(string exe, Run run)
    {
        var until = DateTime.UtcNow + Wait;
        while (true)
        {
            if (Look(exe, run) is { } found)
            {
                return found;
            }

            if (DateTime.UtcNow > until)
            {
                return null;
            }

            Thread.Sleep(100);
        }
    }

    private static CrashEvent? Look(string exe, Run run)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DiagnosticReports");
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var file in new DirectoryInfo(folder).GetFiles(exe + "-*.ips").Where(f => f.LastWriteTimeUtc >= run.Started.UtcDateTime))
        {
            try
            {
                if (Describe(File.ReadAllText(file.FullName), run) is { } found)
                {
                    return found;
                }
            }
            // One that can't be read, or is laid out differently from those seen so far, is passed over
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        return null;
    }

    // The signal, who sent it and the top of the crashed thread's stack, if it's the run's. A process ID is only reused
    // after a restart, which the boot ID tells apart
    internal static CrashEvent? Describe(string ips, Run run)
    {
        var report = JsonSerializer.Deserialize(Body(ips), IpsJson.Default.IpsReport)!;
        if (report.Pid != run.ProcessId
            || run.Boot is not null && report.BootSessionUUID is { } boot && !string.Equals(boot, run.Boot, StringComparison.OrdinalIgnoreCase)
            || !TryParseTime(report.CaptureTime, out var time))
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{report.Exception.Type} ({report.Exception.Signal})");
        if (report.Termination is { ByProc: { } by, ByPid: { } byPid })
        {
            text.Append(CultureInfo.InvariantCulture, $", sent by {by}[{byPid}]");
        }

        text.AppendLine();
        if (report.Exception.Subtype is { } subtype)
        {
            text.AppendLine(subtype);
        }

        foreach (var message in report.Asi?.Values.SelectMany(messages => messages) ?? [])
        {
            text.AppendLine(message);
        }

        if (report.FaultingThread is { } faulting && report.Threads?.ElementAtOrDefault(faulting)?.Frames is { } frames)
        {
            text.Append(CultureInfo.InvariantCulture, $"Thread {faulting} crashed:");
            foreach (var (frame, i) in frames.Take(Frames).Select((frame, i) => (frame, i)))
            {
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"{i,2}  {Frame(frame, report.UsedImages ?? [])}");
            }

            if (frames.Length > Frames)
            {
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"    and {frames.Length - Frames} more");
            }
        }

        return new CrashEvent(time, text.ToString().TrimEnd());
    }

    // Apple's own reader takes the first line as metadata, but it's sometimes left out, and the report then starts the
    // file. The metadata is all on one line, and the report never is
    private static string Body(string ips)
    {
        var firstLine = ips.IndexOf('\n', StringComparison.Ordinal);
        return firstLine > 0 && ips[..firstLine].TrimEnd().EndsWith('}') ? ips[(firstLine + 1)..] : ips;
    }

    private static string Frame(IpsFrame frame, IpsImage[] images)
    {
        var image = frame.ImageIndex is { } index ? images.ElementAtOrDefault(index)?.Name : null;
        var symbol = frame.Symbol is { } name ? $"{name} + {frame.SymbolLocation}" : $"0x{frame.ImageOffset:x}";
        var source = frame.SourceFile is { } file ? $" ({file}:{frame.SourceLine})" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{image ?? "???"}  {symbol}{source}");
    }

    // Such as 2026-11-04 02:09:14.8012 +0000, whose offset has no colon, which .NET's formats all have
    private static bool TryParseTime(string text, out DateTimeOffset time) =>
        DateTimeOffset.TryParseExact(
            text.Length > 2 ? text[..^2] + ":" + text[^2..] : text, "yyyy-MM-dd HH:mm:ss.FFFF zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

// The parts of a crash report read here, from Apple's "Interpreting the JSON format of a crash report", which says a
// report can contain each. Only what a report must have to be the run's, and what crashed it, isn't nullable, so a
// report without them isn't read. BootSessionUUID and the source lines aren't in Apple's list, but macOS 15 writes them
internal sealed record IpsReport(
    int Pid,
    string CaptureTime,
    IpsException Exception,
    int? FaultingThread = null,
    IpsThread[]? Threads = null,
    IpsImage[]? UsedImages = null,
    string? BootSessionUUID = null,
    IpsTermination? Termination = null,
    Dictionary<string, string[]>? Asi = null);

internal sealed record IpsException(string Type, string Signal, string? Subtype = null);

// Who sent the signal: itself, as for abort(), another program, or the kernel's exception handler
internal sealed record IpsTermination(string? ByProc = null, int? ByPid = null);

internal sealed record IpsThread(IpsFrame[]? Frames = null);

// Symbol is left out where the binary has no names for it, as obmd's own code has without its dSYM
internal sealed record IpsFrame(
    int? ImageIndex = null, long? ImageOffset = null, string? Symbol = null, long? SymbolLocation = null, string? SourceFile = null, int? SourceLine = null);

internal sealed record IpsImage(string? Name = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(IpsReport))]
internal sealed partial class IpsJson : JsonSerializerContext;
