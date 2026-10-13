namespace OpenBackupManager.Core;

/// <summary>
/// A token, password or other secret. It shows as *** when it's printed or logged, and has no properties for a
/// serialiser to read, so it can't reach a log or file by accident. Its value is only read through Reveal
/// </summary>
public sealed class Secret(string value)
{
    private readonly string _value = value;

    public string Reveal() => _value;

    public override string ToString() => "***";
}
