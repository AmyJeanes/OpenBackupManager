namespace OpenBackupManager.Core.Platform;

// A store that isn't on this computer, such as Secret Service with no D-Bus session or nothing providing it, so nothing
// saved from here can be in it
public sealed class SecretStoreUnavailableException : InvalidOperationException
{
    public SecretStoreUnavailableException(string message)
        : base(message)
    {
    }

    public SecretStoreUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

// A store that's there but locked, so what's in it can't be listed or removed until it's unlocked
public sealed class SecretStoreLockedException(string message) : InvalidOperationException(message);
