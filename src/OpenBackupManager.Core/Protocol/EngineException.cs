namespace OpenBackupManager.Core.Protocol;

// A problem with the engine or talking to it, with a message for the user in English. The app words the problems it can
// show itself, from Problem
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

    public EngineException(EngineProblem problem, string message, Exception? innerException = null)
        : base(message, innerException) => Problem = problem;

    public EngineProblem Problem { get; }
}

public enum EngineProblem
{
    Other,
    AnotherUserOrAdmin,
    AnotherVersion,
}
