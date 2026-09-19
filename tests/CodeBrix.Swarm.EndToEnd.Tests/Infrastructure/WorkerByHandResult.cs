using System.Collections.Generic;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// What became of a Worker that was started by hand: the code it exited with, and everything it wrote.
/// </summary>
internal sealed class WorkerByHandResult
{
    public WorkerByHandResult(int exitCode, IReadOnlyList<string> lines)
    {
        ExitCode = exitCode;
        Lines = lines;
    }

    /// <summary>The code the process exited with.</summary>
    public int ExitCode { get; }

    /// <summary>Every line it wrote, from both of its output streams, in the order they arrived.</summary>
    public IReadOnlyList<string> Lines { get; }
}
