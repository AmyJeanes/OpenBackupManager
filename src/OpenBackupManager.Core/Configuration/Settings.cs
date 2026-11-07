using OpenBackupManager.Core.Platform;

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
        new("secret-store", SettingKind.Text)
        {
            Summary = "Where this device keeps its secrets, such as the password that protects your sign-ins.",
            Values = "`credential-manager` on Windows, `keychain` on macOS, `secret-service` on Linux, or `file` on any",
            Choices = [.. SecretStoreKinds.NamesOn(CurrentOS.Platform)],
            Default = "The OS's own store",
            DefaultInFull = "`credential-manager` on Windows and `keychain` on macOS. Linux has no default, so one has to be chosen",
            Example = "\"file\"",
            Notes = "`file` keeps them in `secrets.json`, in the engine's own folder (see [Files and folders](files.md)), "
                + "protected by nothing but its permissions, for when the OS's store can't be used, such as on a server or over SSH. "
                + "On Windows and macOS, any program running as you can read what's in the OS's store too. On Linux, `secret-service` is locked whenever you're not signed in with your password, so it can't be read "
                + "with lingering on and nobody signed in, or after an automatic sign-in until it's unlocked. "
                + "To change it once secrets are saved, run `obm secrets migrate` with the store to move them to, which changes this "
                + "too (see [Command line](command-line.md#commands)). Changing it by hand leaves them behind in the old store.",
        },
        new("updates", SettingKind.Section)
        {
            Summary = "How Open Backup Manager keeps itself up to date.",
        },
        new("updates.automatic", SettingKind.Boolean)
        {
            WorksOn = Os.Windows,
            Summary = "Checks for updates when the app starts and every few hours, and downloads them ready to install.",
            Values = "`true`, or `false` to only check when you ask",
            Default = "`true`",
            DefaultInFull = "`true`",
            Example = "false",
            Notes = "While it's off, **Check for updates** in About still checks straight away. Turning it back on checks "
                + "straight away too.",
        },
        new("updates.prereleases", SettingKind.Boolean)
        {
            Summary = "Gets test versions before they're released, so new things can be tried early.",
            Values = "`true` for test versions, or `false` for released versions only",
            Default = "Follows what's installed",
            DefaultInFull = "Follows what's installed, so a test version gets test versions",
            Example = "true",
            Notes = "A change takes effect from the next [check for updates](updates.md): on Windows, the app's next check, which it "
                + "makes every few hours, or **Check for updates** in About, and on macOS and Linux, the next time you run `obm update`.",
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

    // Empty when any text will do
    public IReadOnlyList<string> Choices { get; init; } = [];

    public bool Hidden { get; init; }

    public bool IsSection => Kind is SettingKind.Section or SettingKind.SectionList;

    public string? Section => Path.LastIndexOf('.') is var dot and >= 0 ? Path[..dot] : null;

    // A setting in a list of sections has none, as one env var couldn't say which entry it's for
    public string? EnvVar => IsSection || Section is { } section && Settings.Find(section)!.Kind == SettingKind.SectionList
        ? null
        : "OBM_" + Path.ToUpperInvariant().Replace('.', '_').Replace('-', '_');

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
