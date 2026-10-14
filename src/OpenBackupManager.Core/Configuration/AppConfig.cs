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

    public UpdateSettings Updates { get; init; } = new();

    public IReadOnlyList<RemoteSettings> Remotes { get; init; } = [];
}

public sealed record UpdateSettings
{
    // Left out, updates follow what's installed: a prerelease gets prereleases, a stable version only stable ones
    public bool? Prereleases { get; init; }
}

public sealed record RemoteSettings
{
    public string Id { get; init; } = "";

    public string? Name { get; init; }

    public string? Root { get; init; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[TomlSerializable(typeof(AppConfig))]
internal sealed partial class AppConfigContext : TomlSerializerContext;
