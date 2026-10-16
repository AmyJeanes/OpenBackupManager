namespace OpenBackupManager.Core.Protocol;

// A problem with the engine or talking to it, with a message for the user
public sealed class EngineException : Exception
{
    public EngineException()
    {
    }

    public EngineException(string message)
        : base(message)
    {
    }

    public EngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
