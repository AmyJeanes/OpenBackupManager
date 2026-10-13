namespace OpenBackupManager.ViewModels;

public enum SyncState
{
    Idle,
    Syncing,
    Paused,
    NeedsAttention,
    Error,
}
