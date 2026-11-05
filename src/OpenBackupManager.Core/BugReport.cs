using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenBackupManager.Core;

// The repository's crash report template, filled in with a crash report, as a link to open. Nothing is sent until the
// person submits it. It's a Markdown template with the bug report form's headings rather than the form, as GitHub puts
// back a form field filled in from a link whenever it's edited (https://github.com/orgs/community/discussions/212206)
public static partial class BugReport
{
    // Someone not signed in to GitHub goes through its sign-in page, which carries the link encoded again, and GitHub
    // fails once that's about 7,000 characters (tried Nov 2026). So a link is measured as it is there, by Length
    public const int LongestLink = 6500;

    // The app has its own in its strings, to be translated
    public const string Check = "Sensitive details have been redacted where possible, but check the report before you submit. GitHub issues are public.";

    // GitHub's limit for an issue's title
    private const int LongestTitle = 256;

    // The bug report form's labels, and its hints as comments, which don't show in the issue, so they needn't be removed
    private static readonly (string Heading, string Hint)[] Sections =
    [
        ("What happened", "What were you doing when it closed?"),
        ("Steps to reproduce", "How to make it happen again, if you know"),
        ("What you expected", ""),
        ("Open Backup Manager version", ""),
        ("Operating system", ""),
        ("Remote", "e.g. OneDrive personal"),
        ("Logs", Check),
        ("AI assistance (if any)", "Which tool, and what it did"),
    ];

    private static readonly string Template = typeof(BugReport).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "RepositoryUrl").Value + "/issues/new?template=crash_report.md";

    // With what went wrong as its title, the report's version and OS, and logs under Logs: the report, or what to do
    // instead when it doesn't fit
    public static string Link(string report, string logs)
    {
        var lines = Lines(report);
        var program = lines[0].Split(" closed unexpectedly")[0];
        var summary = CrashReports.Summary(lines);
        var title = summary.Length > 0 ? $"{program} closed unexpectedly: {summary}" : $"{program} closed unexpectedly";
        if (title.Length > LongestTitle)
        {
            title = title[..(LongestTitle - 1)] + "…";
        }

        logs = logs.ReplaceLineEndings("\n").TrimEnd();
        // Longer than any run of backticks in the logs, so none of them ends it
        var fence = new string('`', Math.Max(3, Backticks().Matches(logs).Select(match => match.Length + 1).DefaultIfEmpty().Max()));
        // Folded away, as GitHub shows a code block at its full length
        var folded = $"<details>\n<summary>Crash report</summary>\n\n{fence}text\n{logs}\n{fence}\n\n</details>";
        string[] filledIn = ["", "", "", Line(lines, "Version: "), Line(lines, "OS: "), "", folded, ""];
        var body = string.Join("\n\n", Sections.SelectMany((section, i) => new[]
        {
            $"### {section.Heading}",
            section.Hint.Length > 0 ? $"<!-- {section.Hint} -->" : "",
            filledIn[i],
        }.Where(part => part.Length > 0)));
        return $"{Template}&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }

    public static bool Fits(string report, string logs) => Length(Link(report, logs)) <= LongestLink;

    public static int Length(string link) => Uri.EscapeDataString(link).Length;

    private static string[] Lines(string report) => report.ReplaceLineEndings("\n").TrimEnd().Split('\n');

    private static string Line(string[] lines, string start) =>
        Array.Find(lines, line => line.StartsWith(start, StringComparison.Ordinal))?[start.Length..] ?? "";

    [GeneratedRegex("`+")]
    private static partial Regex Backticks();
}
