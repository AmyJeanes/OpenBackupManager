namespace OpenBackupManager.Engine;

// StartedByApp: the tray app started it, so its Quit stops it
internal sealed record EngineOptions(bool StartedByApp);
