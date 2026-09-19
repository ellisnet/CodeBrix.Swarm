using System;

namespace CodeBrix.Swarm.Core.Messaging;

/// <summary>
/// The two message kinds the swarm defines for itself. Starting and ending Workers is the swarm's
/// own business, so these two are built in; every other kind belongs to the consuming application,
/// which may name them anything that does not begin with the reserved prefix.
/// </summary>
public static class SwarmMessageKinds
{
    /// <summary>
    /// The prefix the swarm keeps for its own kinds. An application's kinds must not start with it.
    /// </summary>
    public const string ReservedPrefix = "swarm.";

    /// <summary>
    /// Sent to every Hive, and it means the work is over: each Hive stops starting Workers for good,
    /// ends the Workers it has, and tells the consuming application it has finished. Nothing resumes
    /// starting Workers afterwards - there is no message that undoes this one.
    /// </summary>
    public const string TerminateAllWorkers = "swarm.terminate-all-workers";

    /// <summary>
    /// Sent to every Worker, and it means end yourself: the Worker stops its work, runs the
    /// application's shutdown step and exits with <see cref="SwarmExitCodes.Success" />.
    /// </summary>
    public const string TerminateWorker = "swarm.terminate-worker";

    /// <summary>
    /// True when the kind is one the swarm defines, rather than one of the application's.
    /// </summary>
    /// <param name="kind">The kind to look at.</param>
    /// <returns>True for a kind the swarm reserves.</returns>
    public static bool IsBuiltIn(string kind)
        => !string.IsNullOrEmpty(kind) && kind.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Returns the built-in kind that tells a process of the given role to end.
    /// </summary>
    /// <param name="role">The role of the receiving process.</param>
    /// <returns>
    /// <see cref="TerminateAllWorkers" /> for a Hive, <see cref="TerminateWorker" /> for a Worker.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The role is not one of the known roles.</exception>
    public static string TerminateKindFor(SwarmRole role)
    {
        return role switch
        {
            SwarmRole.Hive => TerminateAllWorkers,
            SwarmRole.Worker => TerminateWorker,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown swarm role.")
        };
    }
}
