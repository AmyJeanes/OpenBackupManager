using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli;

// obm diagnostics export: a zip of what helps find out what went wrong, to attach to an issue. It only takes what it
// names here, so secrets never go in
internal static class Diagnostics
{
    // As long as obm keeps its logs
    private static readonly TimeSpan Kept = TimeSpan.FromDays(7);

    public static async Task<int> ExportAsync(AppPaths paths, EngineService? service, string? output, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var name = string.Create(CultureInfo.InvariantCulture, $"obm-diagnostics-{now:yyyyMMdd-HHmmss}.zip");
        var file = Path.GetFullPath(output is null ? name : Directory.Exists(output) ? Path.Combine(output, name) : output);
        if (File.Exists(file))
        {
            await Console.Error.WriteLineAsync($"obm: there's already a file at {file}", cancellationToken);
            return 1;
        }

        var summary = await SummaryAsync(paths, service, now, cancellationToken);
        var logs = Directory.Exists(paths.Logs) ? AppPaths.LogFiles.SelectMany(pattern => Directory.GetFiles(paths.Logs, pattern)).Order(StringComparer.Ordinal).ToList() : [];
        var crashes = CrashReports.List(paths);
        var config = File.Exists(paths.ConfigFile);

        // Named once it's complete, so one cut off, as by Ctrl+C or the terminal closing, isn't taken for it
        var partial = file + ".partial";
        FileStream stream;
        try
        {
            stream = new FileStream(partial, FileMode.Create);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"obm: {file} can't be saved: {e.Message}", cancellationToken);
            return 1;
        }

        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Add(zip, "summary.txt", Redaction.Redact(summary), cancellationToken);
                if (config)
                {
                    Add(zip, "config.toml", Redaction.RedactAll(Read(paths.ConfigFile)), cancellationToken);
                }

                foreach (var (entry, text) in SystemRecordsFor(service, now - Kept))
                {
                    Add(zip, entry, Redaction.RedactAll(text), cancellationToken);
                }

                foreach (var log in logs)
                {
                    Add(zip, "logs/" + Path.GetFileName(log), Redaction.RedactAll(Read(log)), cancellationToken);
                }

                foreach (var report in crashes)
                {
                    Add(zip, "crashes/" + Path.GetFileName(report.File), Redaction.Redact(Read(report.File)), cancellationToken);
                }
            }

            File.Move(partial, file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Delete(partial);
            await Console.Error.WriteLineAsync($"obm: {file} can't be saved: {e.Message}", cancellationToken);
            return 1;
        }
        catch (OperationCanceledException)
        {
            File.Delete(partial);
            throw;
        }

        List<string> parts = [];
        if (logs.Count > 0)
        {
            parts.Add("the logs from the last 7 days");
        }

        if (crashes.Count > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{crashes.Count} crash report{(crashes.Count == 1 ? "" : "s")}"));
        }

        if (config)
        {
            parts.Add("config.toml");
        }

        parts.Add("a summary");
        Console.WriteLine($"Saved {file}");
        Console.WriteLine($"It has {string.Join(", ", parts[..^1])}{(parts.Count > 1 ? " and " : "")}{parts[^1]}.");
        Console.WriteLine("Sensitive details have been redacted where possible, but logs can still name your folders and files, so look inside before you share it.");
        return 0;
    }

    // This device's basics, then what obm status and obm config validate print
    private static async Task<string> SummaryAsync(AppPaths paths, EngineService? service, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        text.WriteLine($"Open Backup Manager diagnostics, exported at {now:yyyy-MM-dd HH:mm:ss zzz}");
        text.WriteLine();
        // As a crash report's header, which redaction leaves as it is
        text.WriteLine($"Version: {AppVersion.Current}");
        text.WriteLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
        if (paths.Config == paths.State)
        {
            text.WriteLine($"Home: {paths.Config}");
        }
        else
        {
            text.WriteLine($"Config: {paths.Config}");
            text.WriteLine($"State: {paths.State}");
        }

        text.WriteLine("Service: " + ServiceState(service));
        var variables = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(variable => variable.StartsWith("OBM_", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
        text.WriteLine("OBM_ variables set: " + (variables.Count == 0 ? "none" : string.Join(", ", variables)));

        text.WriteLine();
        text.WriteLine("obm status");
        await Status.RunAsync(paths, service, json: false, text, text, cancellationToken);
        text.WriteLine();
        text.WriteLine("obm config validate");
        Config.Validate(paths, path: null, withEnvironment: false, json: false, text, text);
        return text.ToString();
    }

    private static string ServiceState(EngineService? service) =>
        service is null ? "none, as on Windows the app starts the engine"
        : !service.IsInstalled ? "not installed"
        : service.SwitchedOff ? $"installed, but switched off in {service.WhereToSwitchOn}"
        : service.HasGivenUp ? "installed, but it has stopped starting the engine, which kept failing"
        : $"installed in {service.File}";

    private static IEnumerable<(string Entry, string Text)> SystemRecordsFor(EngineService? service, DateTimeOffset since)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return (
                "windows-events.txt",
                SystemRecords.WindowsEvents(
                    ["OpenBackupManager.App.Windows.exe", "obmd.exe", "obm.exe"],
                    [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppContext.BaseDirectory],
                    since));
        }
        else if (OperatingSystem.IsMacOS())
        {
            foreach (var report in SystemRecords.MacOSReports(["obmd", "obm"], since))
            {
                yield return ("diagnostic-reports/" + Path.GetFileName(report), Read(report));
            }
        }
        else if (service is SystemdService systemd)
        {
            yield return ("journal.txt", SystemRecords.Journal(systemd.Unit, since));
        }
    }

    private static void Add(ZipArchive zip, string entry, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var writer = new StreamWriter(zip.CreateEntry(entry, CompressionLevel.SmallestSize).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    // Logs may still be being written to
    private static string Read(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
