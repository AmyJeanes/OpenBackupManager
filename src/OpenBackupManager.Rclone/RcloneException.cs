namespace OpenBackupManager.Rclone;

public sealed class RcloneException : Exception
{
    // rclone found nothing there, such as a folder to list
    public bool NotFound { get; init; }

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
