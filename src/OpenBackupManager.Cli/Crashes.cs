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
        var reports = CrashReports.List(paths);
        if (reports.ElementAtOrDefault(number - 1) is not { } report)
        {
            Console.Error.WriteLine(
                reports.Count == 0 ? "obm: there aren't any crash reports"
                : string.Create(CultureInfo.InvariantCulture, $"obm: there's no crash report {number}. obm crashes list shows them"));
            return 1;
        }

        Console.WriteLine(report.File);
        Console.WriteLine();
        Console.Write(File.ReadAllText(report.File));
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
}
