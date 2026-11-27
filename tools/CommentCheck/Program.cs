using System.Diagnostics;

// Fails on a comment line longer than 120 characters, naming each, in the files given or every tracked one: // in C#
// and JSON5, # in scripts, YAML, TOML and git's and EditorConfig's files. A line that's one word, such as a long link,
// is let through, as it can't be wrapped
const int Most = 120;

var files = args.Length > 0 ? args : Tracked();
var over = 0;
foreach (var file in files)
{
    if (Marker(file) is not { } marker)
    {
        continue;
    }

    var lines = File.ReadAllLines(file);
    for (var i = 0; i < lines.Length; i++)
    {
        var comment = lines[i].TrimStart();
        if (lines[i].Length > Most && comment.StartsWith(marker, StringComparison.Ordinal) && comment.TrimStart(marker[0]).Trim().Contains(' '))
        {
            Console.Error.WriteLine($"{file}:{i + 1}: this comment is {lines[i].Length} characters, over {Most}");
            over++;
        }
    }
}

return over > 0 ? 1 : 0;

static string? Marker(string file) => Path.GetExtension(file) switch
{
    ".cs" or ".json5" => "//",
    ".ps1" or ".sh" or ".yml" or ".yaml" or ".toml" or ".editorconfig" or ".gitignore" or ".gitattributes" => "#",
    _ => Path.GetFileName(file) is "Dockerfile" or "pre-commit" ? "#" : null,
};

static string[] Tracked()
{
    using var git = Process.Start(new ProcessStartInfo("git", ["ls-files"]) { RedirectStandardOutput = true })!;
    var files = git.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    git.WaitForExit();
    return files;
}
