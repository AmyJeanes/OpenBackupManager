using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// Obmd is the file UpdateWatcher watches, or null for none
public sealed record EngineOptions(EngineStarter StartedBy, string? ServiceFile = null, string? Obmd = null);
