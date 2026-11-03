using System.Globalization;

namespace OpenBackupManager.Core.Configuration;

/// <summary>What reading config.toml found. Config is null when the file has errors</summary>
public sealed record ConfigResult(AppConfig? Config, IReadOnlyList<ConfigProblem> Problems)
{
    public bool HasErrors => Problems.Any(p => p.IsError);
}

/// <summary>
/// An error stops the file being used. A warning, such as an unknown key, doesn't. Value is the key, section or id the
/// problem is about, or for one TOML itself reports, Tomlyn's own message. EnvVar is the environment variable it's in,
/// when it's in one rather than on a line of the file
/// </summary>
public sealed record ConfigProblem(int Line, ConfigProblemKind Kind, bool IsError, string? Value = null, string? EnvVar = null)
{
    // In the given language, for showing to people
    public string Describe(CultureInfo culture) => EnvVar is null
        ? Format(culture, nameof(Strings.ConfigProblem_OnLine), Line, Message(culture))
        : Format(culture, nameof(Strings.ConfigProblem_InEnvVar), EnvVar, Message(culture));

    // Tomlyn's messages are only in English, so they're passed on as they are
    public string Message(CultureInfo culture) => Kind == ConfigProblemKind.Toml ? Value! : Format(culture, Kind switch
    {
        ConfigProblemKind.ShouldBeTrueOrFalse => nameof(Strings.ConfigProblem_ShouldBeTrueOrFalse),
        ConfigProblemKind.ShouldBeWholeNumber => nameof(Strings.ConfigProblem_ShouldBeWholeNumber),
        ConfigProblemKind.ShouldBeText => nameof(Strings.ConfigProblem_ShouldBeText),
        ConfigProblemKind.VersionTooLow => nameof(Strings.ConfigProblem_VersionTooLow),
        ConfigProblemKind.ShouldBeTableArray => nameof(Strings.ConfigProblem_ShouldBeTableArray),
        ConfigProblemKind.ShouldBeTable => nameof(Strings.ConfigProblem_ShouldBeTable),
        ConfigProblemKind.RemoteNeedsId => nameof(Strings.ConfigProblem_RemoteNeedsId),
        ConfigProblemKind.RemoteIdUsedTwice => nameof(Strings.ConfigProblem_RemoteIdUsedTwice),
        ConfigProblemKind.UnknownSetting => nameof(Strings.ConfigProblem_UnknownSetting),
        ConfigProblemKind.UnknownSection => nameof(Strings.ConfigProblem_UnknownSection),
        ConfigProblemKind.NewerVersion => nameof(Strings.ConfigProblem_NewerVersion),
        ConfigProblemKind.NotAChoice => nameof(Strings.ConfigProblem_NotAChoice),
        _ => throw new InvalidOperationException($"No message for {Kind}"),
    }, Value, Kind == ConfigProblemKind.NotAChoice ? string.Join(", ", Settings.Find(Value ?? "")?.Choices ?? []) : null);

    // Always in English, for logs and exceptions
    public override string ToString() => $"{(EnvVar is null ? $"line {Line}" : $"environment variable {EnvVar}")}: {Message(CultureInfo.InvariantCulture)}";

    private static string Format(CultureInfo culture, string name, params object?[] values) =>
        string.Format(culture, Strings.ResourceManager.GetString(name, culture)!, values);
}

public enum ConfigProblemKind
{
    Toml,
    ShouldBeTrueOrFalse,
    ShouldBeWholeNumber,
    ShouldBeText,
    VersionTooLow,
    ShouldBeTableArray,
    ShouldBeTable,
    RemoteNeedsId,
    RemoteIdUsedTwice,
    UnknownSetting,
    UnknownSection,
    NewerVersion,
    NotAChoice,
}
