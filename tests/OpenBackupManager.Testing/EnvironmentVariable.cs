namespace OpenBackupManager.Testing;

// Set until it's disposed, then put back as it was
public sealed class EnvironmentVariable : IDisposable
{
    private readonly string _name;
    private readonly string? _before;

    public EnvironmentVariable(string name, string? value)
    {
        _name = name;
        _before = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _before);
}
