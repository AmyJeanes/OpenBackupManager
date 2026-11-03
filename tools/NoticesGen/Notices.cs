using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace NoticesGen;

public static class Notices
{
    private const string Rule = "--------------------------------------------------------------------------------";

    // Files that load when the program runs. Analyzers and build tools only have other kinds, such as build
    private static readonly string[] ShippedAssets = ["runtime", "native", "resource"];

    // The hand-written notices, then each NuGet package with files in the programs, under its licence. Packages with
    // the same licence text share one copy of it
    public static string Write(string handWritten, IEnumerable<string> assetsFiles, string runtime)
    {
        var groups = assetsFiles
            .SelectMany(file => Shipped(file, runtime))
            .DistinctBy(package => (package.Id, package.Version))
            .GroupBy(package => (package.LicenceName, package.Licence))
            .Select(group => group.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase).ToList())
            .OrderBy(group => group[0].Id, StringComparer.OrdinalIgnoreCase);

        var text = new StringBuilder(handWritten.ReplaceLineEndings("\n").TrimEnd());
        foreach (var group in groups)
        {
            var licence = group[0].LicenceName is { } name ? $"the {name} licence" : "the licence they come with";
            text.Append(CultureInfo.InvariantCulture, $"\n\n{Rule}\n\nThese NuGet packages are used under {licence}:\n\n");
            foreach (var package in group)
            {
                text.Append(CultureInfo.InvariantCulture, $"{package.Id} {package.Version}");
                text.Append(package.Copyright is null ? "\n" : $": {package.Copyright}\n");
            }

            foreach (var package in group.Where(package => package.Notice is not null))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n{package.Id}'s NOTICE file:\n\n{package.Notice}\n");
            }

            text.Append('\n').Append(group[0].Licence);
        }

        return text.Append('\n').ToString().ReplaceLineEndings();
    }

    private static List<Package> Shipped(string assetsFile, string runtime)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = assets.RootElement;
        // Restoring for a runtime adds a target for the framework and runtime together, such as net10.0/linux-x64,
        // where each package's files are those for that runtime alone
        var targets = root.GetProperty("targets").EnumerateObject()
            .Where(target => target.Name.EndsWith($"/{runtime}", StringComparison.Ordinal))
            .ToList();
        if (targets.Count != 1)
        {
            throw new InvalidDataException($"{assetsFile} has no packages restored for {runtime}. Publish for {runtime} first");
        }

        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();
        var packages = new List<Package>();
        foreach (var library in targets[0].Value.EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package" || !Ships(library.Value))
            {
                continue;
            }

            var path = root.GetProperty("libraries").GetProperty(library.Name).GetProperty("path").GetString()!;
            var folder = folders.Select(packageFolder => Path.Combine(packageFolder, path)).FirstOrDefault(Directory.Exists)
                ?? throw new InvalidDataException($"{library.Name} isn't in any package folder {assetsFile} lists. Restore it first");
            var (id, version) = library.Name.Split('/') switch
            {
                [var name, var number] => (name, number),
                _ => throw new InvalidDataException($"{assetsFile} has a library named {library.Name}, not <id>/<version>"),
            };
            packages.Add(Read(folder, id, version));
        }

        return packages;
    }

    private static bool Ships(JsonElement library) =>
        ShippedAssets.Any(kind => library.TryGetProperty(kind, out var files)
            // _._ stands for no files, so a package can say it has none for this framework
            && files.EnumerateObject().Any(file => Path.GetFileName(file.Name) != "_._"));

    private static Package Read(string folder, string id, string version)
    {
        var metadata = XDocument.Load(Directory.GetFiles(folder, "*.nuspec").Single()).Root!
            .Elements().Single(element => element.Name.LocalName == "metadata");
        var copyright = Element("copyright")?.Value.Trim() is { Length: > 0 } line ? line : null;
        var license = Element("license")
            ?? throw new InvalidDataException($"{id} {version} names no licence in its nuspec, so it can't be listed");

        if (license.Attribute("type")?.Value == "file")
        {
            var file = Path.Combine(folder, license.Value.Trim().Replace('\\', '/'));
            return new Package(id, version, copyright, null, Normalise(File.ReadAllText(file)), null);
        }

        var expression = license.Value.Trim();
        var template = Template(expression)
            ?? throw new InvalidDataException(
                $"{id} {version} is under {expression}, which has no template in tools/NoticesGen/Licences. "
                + "Check we can ship it, then add the licence's standard text there, named for the expression");
        // Apache-2.0 asks for the package's NOTICE file to be passed on with it, if it has one
        var notice = expression == "Apache-2.0"
            ? Directory.GetFiles(folder).FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Equals("NOTICE", StringComparison.OrdinalIgnoreCase))
            : null;
        return new Package(id, version, copyright, expression, template, notice is null ? null : Normalise(File.ReadAllText(notice)));

        XElement? Element(string name) => metadata.Elements().SingleOrDefault(element => element.Name.LocalName == name);
    }

    private static string? Template(string expression)
    {
        using var stream = typeof(Notices).Assembly.GetManifestResourceStream(expression);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Normalise(reader.ReadToEnd());
    }

    // So texts that differ only in line endings or trailing blank lines are still grouped as one
    private static string Normalise(string text) => text.ReplaceLineEndings("\n").TrimEnd();

    private sealed record Package(string Id, string Version, string? Copyright, string? LicenceName, string Licence, string? Notice);
}
