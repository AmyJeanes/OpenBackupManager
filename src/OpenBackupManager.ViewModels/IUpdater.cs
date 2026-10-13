namespace OpenBackupManager.ViewModels;

public interface IUpdater
{
    // Builds that weren't installed, such as dev builds, can't update
    bool CanUpdate { get; }

    bool IsTestSource { get; }

    bool UpdateReady { get; }

    string? ReadyVersion { get; }

    // Failed checks don't count
    DateTimeOffset? LastChecked { get; }

    // Reports the download's progress as a percentage
    Task<UpdateCheckResult> CheckNow(Action<int> downloading);
}

public enum UpdateCheckResult
{
    UpToDate,
    UpdateReady,
    Failed,
}
