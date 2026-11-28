using Spectre.Console;

namespace OpenBackupManager.Cli;

// obm's questions: Spectre.Console's, choosing with the arrows and searching as you type, or plain ones a line at a
// time, for screen readers and braille displays, as GitHub's CLI has. Everything shown is escaped, as Spectre reads
// square brackets as styles and rclone's words have them
internal sealed class Prompts(IAnsiConsole console, bool plain)
{
    // Set to anything but 0 or false
    public const string PlainVariable = "OBM_ACCESSIBLE_PROMPTS";

    // Plain too when the terminal can't redraw
    public static Prompts ForThisTerminal()
    {
        var plain = IsPlain(Environment.GetEnvironmentVariable(PlainVariable), Environment.GetEnvironmentVariable("TERM"));
        // Without ANSI, Spectre's spinners and progress print lines rather than redrawing, with no colours
        return new(AnsiConsole.Create(new AnsiConsoleSettings { Ansi = plain ? AnsiSupport.No : AnsiSupport.Detect }), plain);
    }

    internal static bool IsPlain(string? asked, string? term) =>
        (asked is { Length: > 0 } && asked != "0" && !asked.Equals("false", StringComparison.OrdinalIgnoreCase)) || term == "dumb";

    // Not in a script or with its input piped in, where a command names the option to pass instead
    public bool CanAsk => console.Profile.Capabilities.Interactive;

    public T Choose<T>(string question, IReadOnlyList<T> choices, Func<T, string> label)
        where T : notnull
    {
        if (!plain)
        {
            return console.Prompt(
                new SelectionPrompt<T>()
                    .Title(Markup.Escape(question))
                    .EnableSearch()
                    .PageSize(10)
                    .AddChoices(choices)
                    .UseConverter(choice => Markup.Escape(label(choice))));
        }

        // Read once, then answered by its number or name
        console.WriteLine(question);
        for (var i = 0; i < choices.Count; i++)
        {
            console.WriteLine($"  {i + 1}. {label(choices[i])}");
        }

        var answer = console.Prompt(new TextPrompt<string>("Number or name:").Validate(answer => Find(answer) is not null, "That isn't one of them"));
        return choices[Find(answer)!.Value];

        // A name can be its start, if no other starts the same
        int? Find(string answer)
        {
            answer = answer.Trim();
            if (int.TryParse(answer, out var number) && number >= 1 && number <= choices.Count)
            {
                return number - 1;
            }

            var named = Enumerable.Range(0, choices.Count).Where(i => label(choices[i]).Equals(answer, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count == 0 && answer.Length > 0)
            {
                named = [.. Enumerable.Range(0, choices.Count).Where(i => label(choices[i]).StartsWith(answer, StringComparison.OrdinalIgnoreCase))];
            }

            return named.Count == 1 ? named[0] : null;
        }
    }

    public string Ask(string question, string? defaultAnswer = null)
    {
        var prompt = new TextPrompt<string>(Markup.Escape(question)).AllowEmpty();
        return console.Prompt(defaultAnswer is null ? prompt : prompt.DefaultValue(defaultAnswer));
    }

    // Plain shows nothing as it's typed, as a screen reader reads each * aloud
    public string AskSecret(string question) =>
        console.Prompt(new TextPrompt<string>(Markup.Escape(question)).AllowEmpty().Secret(plain ? null : '*'));

    public bool Confirm(string question, bool defaultAnswer) =>
        console.Prompt(new ConfirmationPrompt(Markup.Escape(question)) { DefaultValue = defaultAnswer });
}
