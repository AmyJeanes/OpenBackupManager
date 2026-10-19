namespace OpenBackupManager.Core.Configuration;

/// <summary>
/// Every section and setting config.toml can have. The file is checked against these, and the user guide's Settings
/// page is made from them by tools/DocsGen, so their text is Markdown for the guide
/// </summary>
public static class Settings
{
    public static IReadOnlyList<Setting> All { get; } =
    [
        new("start-at-login", SettingKind.Boolean)
        {
            WorksOn = Os.Windows,
            Summary = "Starts the app in the tray when you sign in.",
            Values = "`true` or `false`",
            Default = "`true` when installed",
            DefaultInFull = "`true` for the installed app, unless it was started with another home",
            Example = "false",
            Notes = "You can also turn it off in Windows' Settings > Apps > Startup, or Task Manager's Startup apps, which the app "
                + "leaves alone even while this is `true`.",
        },
        new("updates", SettingKind.Section)
        {
            Summary = "How the app keeps itself up to date.",
        },
        new("updates.prereleases", SettingKind.Boolean)
        {
            WorksOn = Os.Windows,
            Summary = "Gets test versions before they're released, so new things can be tried early.",
            Values = "`true` for test versions, or `false` for released versions only",
            Default = "Follows what's installed",
            DefaultInFull = "Follows what's installed, so a test version gets test versions",
            Example = "true",
            Notes = "A change takes effect from the next [check for updates](updates.md), which the app makes every few hours, or "
                + "straight away from **Check for updates** in About.",
        },
        // Not used by the app yet, so it's left out of the guide
        new("remotes", SettingKind.SectionList) { Hidden = true },
        new("remotes.id", SettingKind.Text),
        new("remotes.name", SettingKind.Text),
        new("remotes.root", SettingKind.Text),
    ];

    private static readonly Dictionary<string, Setting> ByPath = All.ToDictionary(s => s.Path);

    public static Setting? Find(string path) => ByPath.GetValueOrDefault(path);

    // A setting in a hidden section is hidden too
    public static IEnumerable<Setting> Documented =>
        All.Where(s => !s.Hidden && !(s.Section is { } section && Find(section)!.Hidden));
}

/// <summary>A section of config.toml, or a setting at the top of the file or in a section</summary>
public sealed record Setting(string Path, SettingKind Kind)
{
    public Os WorksOn { get; init; } = Os.All;

    public string Summary { get; init; } = "";

    public string Values { get; init; } = "";

    // Short, for the guide's table of settings
    public string Default { get; init; } = "";

    public string DefaultInFull { get; init; } = "";

    // The value only, as it's written in the file
    public string Example { get; init; } = "";

    public string Notes { get; init; } = "";

    public bool Hidden { get; init; }

    public bool IsSection => Kind is SettingKind.Section or SettingKind.SectionList;

    public string? Section => Path.LastIndexOf('.') is var dot and >= 0 ? Path[..dot] : null;

    public string Key => Path[(Path.LastIndexOf('.') + 1)..];

    // How it's written in the file, with brackets for a section
    public string Heading => Kind switch
    {
        SettingKind.Section => $"[{Path}]",
        SettingKind.SectionList => $"[[{Path}]]",
        _ => Key,
    };

    // The example as it would be in the file, under its section's line
    public string ExampleText => Section is { } section
        ? $"{Settings.Find(section)!.Heading}\n{Key} = {Example}"
        : $"{Key} = {Example}";
}

public enum SettingKind
{
    Boolean,
    Text,
    Section,
    SectionList,
}

[Flags]
public enum Os
{
    Windows = 1,
    MacOS = 2,
    Linux = 4,
    All = Windows | MacOS | Linux,
}
