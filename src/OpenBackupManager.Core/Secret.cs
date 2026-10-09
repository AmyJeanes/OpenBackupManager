namespace OpenBackupManager.Core;

/// <summary>
/// A token, password or other secret. It shows as *** wherever it's printed, logged or serialised, so it can't reach
/// a log by accident. Its value is only read through Reveal
/// </summary>
public sealed class Secret(string value)
{
    private readonly string _value = value;

    public string Reveal() => _value;

    public override string ToString() => "***";
}
