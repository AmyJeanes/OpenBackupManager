using System.Text.RegularExpressions;

namespace OpenBackupManager.Core;

// Takes personal details out of a crash report or a log before it's shared: the home folder, the user and computer names, and
// the values in web addresses, which can carry tokens. It can't know everything personal, so people check it too
public static partial class Redaction
{
    public static string Redact(string report) =>
        Redact(report, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);

    // For what isn't a report, such as a log, which has no header to leave
    public static string RedactAll(string text) =>
        Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName, header: false);

    // The header is left as it was written, as it holds nothing personal and a name can be an ordinary word: a default
    // Ubuntu computer is named ubuntu
    internal static string Redact(string report, string home, string user, string computer, bool header = true)
    {
        var net = header ? report.IndexOf("\n.NET: ", StringComparison.Ordinal) : -1;
        var end = net < 0 ? -1 : report.IndexOf('\n', net + 1);
        var start = net < 0 ? 0 : end < 0 ? report.Length : end + 1;

        var details = Password().Replace(report[start..], "<removed>");
        details = Query().Replace(details, match => match.Groups["address"].Value + "?" + string.Join('&', match.Groups["query"].Value
            .Split('&')
            .Select(part => part.Length == 0 ? part : part.Split('=', 2) is [var name, _] ? name + "=<removed>" : "<removed>")));

        // Windows and macOS ignore case in paths, and either slash works in Windows' paths
        var folders = home.TrimEnd('\\', '/');
        if (folders.Length > 0)
        {
            details = Regex.Replace(
                details,
                string.Join(@"[\\/]", folders.Split('\\', '/').Select(Regex.Escape)) + @"(?!\w)",
                "~",
                RegexOptions.CultureInvariant | (OperatingSystem.IsLinux() ? RegexOptions.None : RegexOptions.IgnoreCase));
        }

        // In one go, so a user name that's part of the computer's, as in AMY-PC, doesn't leave the rest behind. One of
        // under 3 letters, such as Raspberry Pi's pi, would take out ordinary words while saying little about anyone
        List<string> names = [];
        if (computer.Length >= 3)
        {
            names.Add($"(?<computer>{Regex.Escape(computer)})");
        }

        if (user.Length >= 3)
        {
            names.Add($"(?<user>{Regex.Escape(user)})");
        }

        // The headings obm writes in the report are left too, as a Mac can be named macOS
        if (names.Count > 0)
        {
            var name = new Regex($@"(?<!\w)(?:{string.Join('|', names)})(?!\w)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            details = string.Join('\n', details.Split('\n').Select(line => CrashReports.Headings.Contains(line.TrimEnd('\r'))
                ? line
                : name.Replace(line, match => match.Groups["computer"].Success ? "<computer>" : "<user>")));
        }

        return report[..start] + details;
    }

    // A password written into a web address, as in https://user:password@host
    [GeneratedRegex(@"(?<=\b[a-z][a-z0-9+.-]*://[^\s/?#@:]*:)[^\s/?#@]+(?=@)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Password();

    [GeneratedRegex(@"(?<address>\b[a-z][a-z0-9+.-]*://[^\s?#""'<>()]*)\?(?<query>[^\s#""'<>()\[\]{}]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Query();
}
