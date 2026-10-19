using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace OpenBackupManager.Core.Configuration;

/// <summary>
/// Reads and writes config.toml. A write changes one value in place, so comments and layout written by hand stay,
/// and moves the new file over the old one in one step, keeping the last one as config.toml.bak
/// </summary>
public sealed class ConfigFile(string path)
{
    public const string DefaultText = """
        # Open Backup Manager's settings for this device. The app writes this file, and you can edit it too.
        # Anything left out has its default.
        version = 1

        # start-at-login = false    # Start the app when you sign in. Left out, it does when installed unless a custom home folder is used

        # [updates]
        # prereleases = true    # Get test versions early. Left out, updates follow what's installed

        """;

    public string Path { get; } = path;

    public ConfigResult Read() => ReadText(Path) is { } text ? Parse(text) : new ConfigResult(new AppConfig(), []);

    // Writes the starting file if there isn't one, so there's something to edit
    public void CreateIfMissing()
    {
        if (!File.Exists(Path))
        {
            Write(DefaultText.ReplaceLineEndings("\n"));
        }
    }

    // Null if there's no file
    internal static string? ReadText(string path)
    {
        try
        {
            using var reader = new StreamReader(OpenShared(path));
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    // Lets others write to or delete the file while it's open, so an editor's save doesn't fail because it's being read
    internal static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static ConfigResult Parse(string text)
    {
        var document = SyntaxParser.Parse(text);
        var problems = document.Diagnostics
            .Select(d => new ConfigProblem(d.Span.Start.Line + 1, ConfigProblemKind.Tomlyn, d.Kind == DiagnosticMessageKind.Error, d.Message))
            .ToList();
        if (!document.HasErrors)
        {
            Check(document, problems);
        }

        if (problems.Any(p => p.IsError))
        {
            return new ConfigResult(null, problems);
        }

        AppConfig config;
        try
        {
            config = TomlSerializer.Deserialize(text, AppConfigContext.Default.AppConfig) ?? new AppConfig();
        }
        catch (TomlException e)
        {
            // Only for shapes the checks above leave alone, such as a section written as an inline table
            problems.Add(new ConfigProblem(e.Line ?? 1, ConfigProblemKind.Tomlyn, IsError: true, e.Message));
            return new ConfigResult(null, problems);
        }

        if (config.Version > AppConfig.CurrentVersion)
        {
            problems.Add(new ConfigProblem(Line(document.KeyValues.First(kv => Name(kv.Key) == "version")), ConfigProblemKind.NewerVersion, IsError: false));
        }

        return new ConfigResult(config, problems);
    }

    // Changes one setting, or removes it when the value is null
    public void Set(string? table, string key, object? value)
    {
        var text = ReadText(Path) ?? DefaultText.ReplaceLineEndings("\n");
        var result = Parse(text);
        if (result.HasErrors)
        {
            throw new ConfigException($"config.toml can't be changed until it's fixed: {result.Problems.First(p => p.IsError)}");
        }

        if (result.Config!.Version > AppConfig.CurrentVersion)
        {
            throw new ConfigException("config.toml was written by a newer version of the app, so this version won't change it");
        }

        var updated = Edit(text, table, key, value);
        if (Parse(updated) is { HasErrors: true } check)
        {
            throw new InvalidOperationException($"Changing {key} would break config.toml: {check.Problems.First(p => p.IsError)}");
        }

        Write(updated);
    }

    internal static string Edit(string text, string? table, string key, object? value)
    {
        var document = SyntaxParser.Parse(text);
        var section = table is null ? null : document.Tables.OfType<TableSyntax>().FirstOrDefault(t => Name(t.Name) == table);
        IEnumerable<KeyValueSyntax> items = table is null ? document.KeyValues : section?.Items ?? [];
        var existing = items.FirstOrDefault(kv => Name(kv.Key) == key);
        if (existing is not null)
        {
            if (value is null)
            {
                var start = LineStart(text, existing.Span.Start.Offset);
                return text.Remove(start, LineEnd(text, existing.Span.End.Offset) - start);
            }

            var replacement = Value(value);
            // The old value's trivia holds the spacing and any comment after it
            Token(replacement).LeadingTrivia = Token(existing.Value!).LeadingTrivia;
            Token(replacement).TrailingTrivia = Token(existing.Value!).TrailingTrivia;
            existing.Value = replacement;
            return document.ToString();
        }

        if (value is null)
        {
            return text;
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var line = $"{key} = {Value(value)}{newLine}";
        if (table is null)
        {
            return document.KeyValues.LastOrDefault() is { } last ? text.Insert(LineEnd(text, last.Span.End.Offset), line) : line + text;
        }

        if (section is not null)
        {
            var after = section.Items.LastOrDefault() is { } lastItem ? lastItem.Span.End.Offset : section.Span.Start.Offset;
            return text.Insert(LineEnd(text, after), line);
        }

        var ending = text.Length == 0 || text.EndsWith('\n') ? "" : newLine;
        var gap = text.Length == 0 ? "" : newLine;
        return text + ending + gap + $"[{table}]{newLine}" + line;
    }

    private static void Check(DocumentSyntax document, List<ConfigProblem> problems)
    {
        foreach (var kv in document.KeyValues)
        {
            CheckKey(Name(kv.Key), kv, problems);
        }

        var ids = new HashSet<string>();
        foreach (var table in document.Tables)
        {
            var name = Name(table.Name);
            if (Settings.Find(name) is not { IsSection: true } section)
            {
                problems.Add(new ConfigProblem(Line(table), ConfigProblemKind.UnknownSection, IsError: false, name));
                continue;
            }

            var isList = section.Kind == SettingKind.SectionList;
            if (isList != table is TableArraySyntax)
            {
                problems.Add(new ConfigProblem(Line(table), isList ? ConfigProblemKind.ShouldBeTableArray : ConfigProblemKind.ShouldBeTable, IsError: true, name));
                continue;
            }

            foreach (var kv in table.Items)
            {
                CheckKey(name + "." + Name(kv.Key), kv, problems);
            }

            if (name == "remotes")
            {
                var id = table.Items.FirstOrDefault(kv => Name(kv.Key) == "id");
                if (id?.Value is not StringValueSyntax { Value: { Length: > 0 } value })
                {
                    problems.Add(new ConfigProblem(Line(table), ConfigProblemKind.RemoteNeedsId, IsError: true));
                }
                else if (!ids.Add(value))
                {
                    problems.Add(new ConfigProblem(Line(id), ConfigProblemKind.RemoteIdUsedTwice, IsError: true, value));
                }
            }
        }
    }

    private static void CheckKey(string path, KeyValueSyntax kv, List<ConfigProblem> problems)
    {
        // Not a setting: which layout the file is in, so a newer one isn't misread
        if (path == "version")
        {
            if (kv.Value is not IntegerValueSyntax number)
            {
                problems.Add(new ConfigProblem(Line(kv), ConfigProblemKind.ShouldBeWholeNumber, IsError: true, path));
            }
            else if (number.Value < 1)
            {
                problems.Add(new ConfigProblem(Line(kv), ConfigProblemKind.VersionTooLow, IsError: true));
            }

            return;
        }

        var setting = Settings.Find(path);
        if (setting is null)
        {
            problems.Add(new ConfigProblem(Line(kv), ConfigProblemKind.UnknownSetting, IsError: false, path));
            return;
        }

        // A section written as a dotted key or inline table is left to the reader
        if (setting.IsSection)
        {
            return;
        }

        var (matches, expected) = setting.Kind switch
        {
            SettingKind.Boolean => (kv.Value is BooleanValueSyntax, ConfigProblemKind.ShouldBeTrueOrFalse),
            _ => (kv.Value is StringValueSyntax, ConfigProblemKind.ShouldBeText),
        };
        if (!matches)
        {
            problems.Add(new ConfigProblem(Line(kv), expected, IsError: true, path));
        }
    }

    private static ValueSyntax Value(object value) => value switch
    {
        bool b => new BooleanValueSyntax(b),
        int i => new IntegerValueSyntax(i),
        long l => new IntegerValueSyntax(l),
        string s => new StringValueSyntax(s),
        _ => throw new ArgumentException($"config.toml can't hold a {value.GetType().Name}", nameof(value)),
    };

    // Only plain values are changed in place, which every setting so far is
    private static SyntaxToken Token(ValueSyntax value) => value switch
    {
        BooleanValueSyntax b => b.Token!,
        IntegerValueSyntax i => i.Token!,
        StringValueSyntax s => s.Token!,
        _ => throw new NotSupportedException($"Can't change a {value.GetType().Name} in place"),
    };

    // Keys are compared without spacing or quotes
    private static string Name(KeySyntax? key) =>
        string.Join('.', (key?.ToString() ?? "").Split('.').Select(part => part.Trim().Trim('"', '\'')));

    private static int Line(SyntaxNode node) => node.Span.Start.Line + 1;

    private static int LineStart(string text, int offset) => text.LastIndexOf('\n', Math.Max(offset - 1, 0)) + 1;

    private static int LineEnd(string text, int offset)
    {
        var end = text.IndexOf('\n', Math.Clamp(offset - 1, 0, text.Length));
        return end < 0 ? text.Length : end + 1;
    }

    private void Write(string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, text);
        if (File.Exists(Path))
        {
            File.Copy(Path, Path + ".bak", overwrite: true);
        }

        // One rename over the old file, so anything watching it never sees it missing, as it would with File.Replace.
        // Windows refuses it while anything has the file open, even shared, so a read in progress is waited out
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                File.Move(temporary, Path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && DateTime.UtcNow < until)
            {
                Thread.Sleep(20);
            }
        }
    }
}

public sealed class ConfigException(string message) : Exception(message);
