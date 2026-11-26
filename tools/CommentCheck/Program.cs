using System.Diagnostics;

// Fails on a C# comment line longer than 120 characters, naming each, in the files given or every tracked one. A line
// that's one word, such as a long link, is let through, as it can't be wrapped
const int Most = 120;

var files = args.Length > 0 ? args : Tracked();
var over = 0;
foreach (var file in files)
{
    var lines = File.ReadAllLines(file);
    for (var i = 0; i < lines.Length; i++)
    {
        var comment = lines[i].TrimStart();
        if (lines[i].Length > Most && comment.StartsWith("//", StringComparison.Ordinal) && comment.TrimStart('/').Trim().Contains(' '))
        {
            Console.Error.WriteLine($"{file}:{i + 1}: this comment is {lines[i].Length} characters, over {Most}");
            over++;
        }
    }
}

return over > 0 ? 1 : 0;

static string[] Tracked()
{
    using var git = Process.Start(new ProcessStartInfo("git", ["ls-files", "*.cs"]) { RedirectStandardOutput = true })!;
    var files = git.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    git.WaitForExit();
    return files;
}
