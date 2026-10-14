namespace OpenBackupManager.Core.Configuration;

/// <summary>What reading config.toml found. Config is null when the file has errors</summary>
public sealed record ConfigResult(AppConfig? Config, IReadOnlyList<ConfigProblem> Problems)
{
    public bool HasErrors => Problems.Any(p => p.IsError);
}

/// <summary>An error stops the file being used. A warning, such as an unknown key, doesn't</summary>
public sealed record ConfigProblem(int Line, string Message, bool IsError)
{
    public override string ToString() => $"line {Line}: {Message}";
}
