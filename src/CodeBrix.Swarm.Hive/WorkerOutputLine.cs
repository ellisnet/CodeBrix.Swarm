namespace CodeBrix.Swarm.Hive;

/// <summary>
/// One line a Worker wrote, when the consuming application asked the Hive to collect what its
/// Workers write.
/// </summary>
/// <remarks>
/// Collecting is off unless it is asked for, and a Worker's output then goes wherever the Hive's own
/// output goes. When it IS asked for, the Hive reads both streams continuously for as long as the
/// Worker lives - a redirected stream that nobody reads fills up, and a Worker whose output stream
/// is full stops running until somebody empties it.
/// </remarks>
public sealed class WorkerOutputLine
{
    internal WorkerOutputLine(string workerId, int processId, bool isError, string text)
    {
        WorkerId = workerId;
        ProcessId = processId;
        IsError = isError;
        Text = text;
    }

    /// <summary>What the Worker that wrote the line was called.</summary>
    public string WorkerId { get; }

    /// <summary>The operating system's number for the Worker's process.</summary>
    public int ProcessId { get; }

    /// <summary>True when the line came from the error stream rather than the ordinary one.</summary>
    public bool IsError { get; }

    /// <summary>The line, without the line break.</summary>
    public string Text { get; }
}
