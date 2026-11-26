using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace OpenBackupManager.Core.Configuration;

/// <summary>This device's settings, from config.toml. Anything left out of the file has its default</summary>
public sealed record AppConfig
{
    // Goes up only when a change would be misread by an older version. Adding a setting doesn't change it
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    // Left out, only the installed app's own home starts at login, so any other home has to ask for it
    public bool? StartAtLogin { get; init; }

    // Off, the app's Quit doesn't ask first
    public bool AskBeforeQuitting { get; init; } = true;

    // Left out, the OS's own store, or on Linux, none until obm asks
    public string? SecretStore { get; init; }

    public UpdateSettings Updates { get; init; } = new();

    public IReadOnlyList<RemoteSettings> Remotes { get; init; } = [];
}

public sealed record UpdateSettings
{
    // Off, it only checks when asked, in About
    public bool Automatic { get; init; } = true;

    // Left out, updates follow what's installed: a prerelease gets prereleases, a stable version only stable ones
    public bool? Prereleases { get; init; }
}

public sealed record RemoteSettings
{
    public string Id { get; init; } = "";

    public string? Name { get; init; }

    // Always given, as config.toml needs it. Empty is the remote's top
    public string Root { get; init; } = "";

    // It names the remote's own files, such as its rclone config, on any OS, as config.toml can be copied to another
    public static bool IsValidId(string id) =>
        id.Length is > 0 and <= 32 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
        && FileNames.Check(id, allPlatforms: true) is null;
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[TomlSerializable(typeof(AppConfig))]
internal sealed partial class AppConfigContext : TomlSerializerContext;
