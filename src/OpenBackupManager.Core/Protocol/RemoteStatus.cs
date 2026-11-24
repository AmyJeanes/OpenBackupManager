using System.Globalization;
using System.Text.Json.Serialization;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Core.Protocol;

/// <summary>
/// How one remote's rclone is doing. Reason says why it isn't running, and Value fills in its message: the log that
/// says more, an exit code, the secret store as secret-store names it, or an error's own message, which is only in
/// English. NextTry is when one retrying or waiting tries again
/// </summary>
public sealed record RemoteStatus(string Id, RemoteState State, RemoteReason? Reason = null, string? Value = null, DateTimeOffset? NextTry = null)
{
    // On the wire, for scripts and other programs to show. The app and obm word it themselves, in the user's language
    [JsonPropertyName("message")]
    public string? InEnglish => Message(CultureInfo.InvariantCulture);

    public string? Message(CultureInfo culture) => Reason switch
    {
        null => null,
        RemoteReason.NoConfig => Format(culture, nameof(Strings.RemoteReason_NoConfig)),
        RemoteReason.NoPassword => Format(culture, nameof(Strings.RemoteReason_NoPassword)),
        RemoteReason.WrongPassword => Format(culture, nameof(Strings.RemoteReason_WrongPassword)),
        RemoteReason.ConfigUnreadable => Format(culture, nameof(Strings.RemoteReason_ConfigUnreadable)),
        RemoteReason.Exited => Format(culture, nameof(Strings.RemoteReason_Exited)),
        RemoteReason.CouldntStart => Format(culture, nameof(Strings.RemoteReason_CouldntStart)),
        RemoteReason.Bug => Format(culture, nameof(Strings.RemoteReason_Bug)),
        RemoteReason.SecretStoreLocked => Format(culture, nameof(Strings.RemoteReason_SecretStoreLocked), SecretStoreKinds.Parse(Value ?? "")?.Title() ?? Value),
        _ => throw new InvalidOperationException($"No message for {Reason}"),
    };

    private string Format(CultureInfo culture, string name) => Format(culture, name, Value);

    private static string Format(CultureInfo culture, string name, string? value) =>
        string.Format(culture, Strings.ResourceManager.GetString(name, culture)!, value);
}

public enum RemoteState
{
    Starting,
    Running,

    // Waiting to start it again, after it stopped or couldn't start
    Retrying,

    // Not tried again until it's set up again, or asked to try
    NeedsSetup,

    // By a bug, until it's asked to try again
    Stopped,

    // Until the secret store with its password is unlocked, which it never asks for
    WaitingForSecrets,
}

public enum RemoteReason
{
    NoConfig,
    NoPassword,
    WrongPassword,
    ConfigUnreadable,
    Exited,
    CouldntStart,
    Bug,
    SecretStoreLocked,
}
