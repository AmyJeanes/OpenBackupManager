using System.Globalization;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli;

// obm crashes: the reports the app and the engine write when they close unexpectedly
internal static class Crashes
{
    public static int List(AppPaths paths, bool json)
    {
        var reports = CrashReports.List(paths);
        if (json)
        {
            Console.WriteLine(Json.Format(reports, CliJson.Default.IReadOnlyListSavedReport));
            return 0;
        }

        if (reports.Count == 0)
        {
            Console.WriteLine("There aren't any crash reports");
            return 0;
        }

        foreach (var report in reports)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{report.Number}. {(report.New ? "(new) " : "")}{report.Heading}"));
            if (report.Summary.Length > 0)
            {
                Console.WriteLine("   " + report.Summary);
            }
        }

        Console.WriteLine();
        Console.WriteLine("See one with obm crashes show <number>");
        return 0;
    }

    public static int Show(AppPaths paths, int number)
    {
        if (Find(paths, number) is not { } report)
        {
            return 1;
        }

        Console.WriteLine(report.File);
        Console.WriteLine();
        Console.Write(File.ReadAllText(report.File));
        CrashReports.MarkSeen(paths, report);
        return 0;
    }

    // Prints what it shares, then opens a new GitHub issue with it filled in, or prints the link where there's no browser
    // to open it in
    public static int Report(AppPaths paths, int number, bool noBrowser)
    {
        if (Find(paths, number) is not { } report)
        {
            return 1;
        }

        var shared = Redaction.Redact(File.ReadAllText(report.File));
        Console.WriteLine(shared.TrimEnd());
        Console.WriteLine();

        var logs = shared;
        if (!BugReport.Fits(shared, shared))
        {
            string whole;
            try
            {
                whole = SaveCopy(report, shared);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"obm: it's too long to fill in, and it couldn't be saved to paste in instead. {e.Message}");
                return 1;
            }

            Console.WriteLine($"It's too long to fill in, so it's saved in {whole}. Paste it under Logs in the issue.");
            logs = Redaction.Redact($"⚠️ PASTE THE CRASH REPORT HERE. It was too long to fill in, so it's saved in {whole}.");
        }

        Console.WriteLine(BugReport.Check);
        var link = BugReport.Link(shared, logs);
        if (!noBrowser && Browser.CanOpen(Environment.GetEnvironmentVariable) && Browser.TryOpen(link))
        {
            Console.WriteLine("Opening a new GitHub issue with it in your browser. Nothing is sent until you submit it there.");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"If it doesn't open, obm crashes report {(number == 1 ? "" : $"{number} ")}--no-browser prints the link."));
        }
        else
        {
            Console.WriteLine("Open this to start a GitHub issue with it. Nothing is sent until you submit it there:");
            Console.WriteLine(link);
        }

        CrashReports.MarkSeen(paths, report);
        return 0;
    }

    // For obm status, which never marks them seen, so a script running it doesn't use them up
    public static IEnumerable<string> New(AppPaths paths)
    {
        var reports = CrashReports.List(paths).Where(report => report.New).ToList();
        if (reports.Count == 0)
        {
            return [];
        }

        return
        [
            "",
            "New crash reports:",
            .. reports.Select(report => string.Create(CultureInfo.InvariantCulture, $"  {report.Number}. {report.Heading}")),
            reports.Count == 1 ? "See it with obm crashes show" : "See one with obm crashes show <number>",
        ];
    }

    // In a new folder only this user can open, as the temp folder may be shared
    private static string SaveCopy(SavedReport report, string text)
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("obm-crash-report-").FullName, Path.GetFileName(report.File));
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var writer = new StreamWriter(file, options);
        writer.Write(text);
        return file;
    }

    private static SavedReport? Find(AppPaths paths, int number)
    {
        var reports = CrashReports.List(paths);
        if (reports.ElementAtOrDefault(number - 1) is { } report)
        {
            return report;
        }

        Console.Error.WriteLine(
            reports.Count == 0 ? "obm: there aren't any crash reports"
            : string.Create(CultureInfo.InvariantCulture, $"obm: there's no crash report {number}. See them with obm crashes list"));
        return null;
    }
}
