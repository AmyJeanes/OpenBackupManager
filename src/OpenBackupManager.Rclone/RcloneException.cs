namespace OpenBackupManager.Rclone;

public sealed class RcloneException : Exception
{
    public RcloneException()
    {
    }

    public RcloneException(string message)
        : base(message)
    {
    }

    public RcloneException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
