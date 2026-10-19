using System.Globalization;
using System.Text;
using OpenBackupManager.Core.Configuration;

// Writes the user guide's reference to every setting into its Settings page, between the generated:settings markers
const string Start = "<!-- generated:settings -->";
const string End = "<!-- /generated:settings -->";

var page = Path.Combine(FindRepo(), "docs", "guide", "settings.md");
var text = File.ReadAllText(page);
var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
var start = text.IndexOf(Start, StringComparison.Ordinal);
var end = text.IndexOf(End, StringComparison.Ordinal);
if (start < 0 || end < start)
{
    Console.Error.WriteLine($"{page} needs the lines {Start} and {End} around where the settings go");
    return 1;
}

var generated = Render().ReplaceLineEndings(newLine);
var updated = text[..(start + Start.Length)] + newLine + newLine + generated + newLine + text[end..];
if (updated != text)
{
    File.WriteAllText(page, updated);
    Console.WriteLine($"Updated {page}");
}

return 0;

static string FindRepo()
{
    for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
    {
        if (File.Exists(Path.Combine(folder.FullName, "OpenBackupManager.slnx")))
        {
            return folder.FullName;
        }
    }

    throw new InvalidOperationException("Run this from inside the repository");
}

static string Render()
{
    var settings = Settings.Documented.ToList();
    var page = new StringBuilder();
    page.Append("| Setting | Default | Works on |\n| --- | --- | --- |\n");
    foreach (var setting in settings.Where(s => !s.IsSection))
    {
        page.Append(CultureInfo.InvariantCulture, $"| [`{setting.Path}`](#{setting.Path}) | {setting.Default} | {WorksOn(setting.WorksOn)} |\n");
    }

    foreach (var setting in settings)
    {
        // A section's settings are a level below it
        var level = setting.Section is null ? "###" : "####";
        page.Append(CultureInfo.InvariantCulture, $"\n{level} `{setting.Heading}` {{#{setting.Path}}}\n\n");
        if (setting.WorksOn != Os.All)
        {
            page.Append(CultureInfo.InvariantCulture, $"<span class=\"obm-badge\" title=\"Works on\" markdown>{Badge(setting.WorksOn)}</span>\n\n");
        }

        page.Append(setting.Summary).Append('\n');
        if (!setting.IsSection)
        {
            page.Append("\n<div class=\"obm-facts\" markdown>\n\n");
            page.Append(CultureInfo.InvariantCulture, $"Values\n:   {setting.Values}\n\nDefault\n:   {setting.DefaultInFull}\n\n</div>\n\n");
            page.Append(CultureInfo.InvariantCulture, $"```toml\n{setting.ExampleText}\n```\n");
        }

        if (setting.Notes.Length > 0)
        {
            page.Append('\n').Append(setting.Notes).Append('\n');
        }
    }

    return page.ToString();
}

static IEnumerable<Os> Each(Os worksOn) => new[] { Os.Windows, Os.MacOS, Os.Linux }.Where(os => worksOn.HasFlag(os));

static string Name(Os os) => os == Os.MacOS ? "macOS" : os.ToString();

static string WorksOn(Os worksOn) => worksOn == Os.All ? "All" : string.Join(", ", Each(worksOn).Select(Name));

static string Badge(Os worksOn)
{
    var icons = string.Join(" ", Each(worksOn).Select(os => os switch
    {
        Os.Windows => ":fontawesome-brands-windows:",
        Os.MacOS => ":fontawesome-brands-apple:",
        _ => ":fontawesome-brands-linux:",
    }));
    var names = Each(worksOn).Select(Name).ToList();
    return names.Count == 1 ? $"{icons} {names[0]} only" : $"{icons} {string.Join(" and ", names)}";
}
