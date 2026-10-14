using System.Globalization;

namespace OpenBackupManager.Core.Configuration;

/// <summary>What reading config.toml found. Config is null when the file has errors</summary>
public sealed record ConfigResult(AppConfig? Config, IReadOnlyList<ConfigProblem> Problems)
{
    public bool HasErrors => Problems.Any(p => p.IsError);
}

/// <summary>
/// An error stops the file being used. A warning, such as an unknown key, doesn't. Value is the key, section or id the
/// problem is about, or for a Tomlyn error, Tomlyn's own message
/// </summary>
public sealed record ConfigProblem(int Line, ConfigProblemKind Kind, bool IsError, string? Value = null)
{
    // In the given language, for showing to people
    public string Describe(CultureInfo culture) => Format(culture, nameof(Strings.ConfigProblem_OnLine), Line, Message(culture));

    // Tomlyn's messages are only in English, so they're passed on as they are
    public string Message(CultureInfo culture) => Kind == ConfigProblemKind.Tomlyn ? Value! : Format(culture, Kind switch
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
        _ => throw new InvalidOperationException($"No message for {Kind}"),
    }, Value);

    // Always in English, for logs and exceptions
    public override string ToString() => $"line {Line}: {Message(CultureInfo.InvariantCulture)}";

    private static string Format(CultureInfo culture, string name, params object?[] values) =>
        string.Format(culture, Strings.ResourceManager.GetString(name, culture)!, values);
}

public enum ConfigProblemKind
{
    Tomlyn,
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
}
