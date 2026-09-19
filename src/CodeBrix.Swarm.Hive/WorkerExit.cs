using System;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Hive;

/// <summary>
/// A Worker this Hive started has ended, and what it ended with. The consuming application is told
/// so that it can put the work back, record it, or simply count.
/// </summary>
/// <remarks>
/// The Hive has already done everything it does about the ending by the time the application is
/// told: the Worker has been let go of, and a failure that happened straight after the start has
/// already set the Hive's wait before the next one.
/// </remarks>
public sealed class WorkerExit
{
    internal WorkerExit(string workerId, int processId, int exitCode, TimeSpan ran, bool wasImmediateFailure)
    {
        WorkerId = workerId;
        ProcessId = processId;
        ExitCode = exitCode;
        Ran = ran;
        WasImmediateFailure = wasImmediateFailure;
    }

    /// <summary>What the Worker was called. Every Worker a Hive starts gets a different one.</summary>
    public string WorkerId { get; }

    /// <summary>
    /// The operating system's number for the process. It is only good while the process exists, and
    /// the process is gone by the time this is read: it is here for matching up records afterwards.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    /// The code the process exited with. The swarm's own codes are on <see cref="SwarmExitCodes" />;
    /// anything else came from the application's work or from the operating system.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>How long the Worker ran, from the moment it was started.</summary>
    public TimeSpan Ran { get; }

    /// <summary>
    /// True when the code is anything but zero. A Worker that finished its work, was told to end, or
    /// was ended by this Hive exits with zero.
    /// </summary>
    public bool IsFailure => !SwarmExitCodes.IsSuccess(ExitCode);

    /// <summary>
    /// True when the Worker failed almost as soon as it started. That is the case the Hive waits
    /// after: starting Worker after Worker that cannot survive its first seconds does nothing but
    /// keep the host busy.
    /// </summary>
    public bool WasImmediateFailure { get; }
}
