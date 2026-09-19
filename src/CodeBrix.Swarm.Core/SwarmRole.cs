namespace CodeBrix.Swarm.Core;

/// <summary>
/// The part a connected process plays in a swarm. The coordinator keeps one hub for each of these,
/// and the access token a process carries is only good for its own role's hub.
/// </summary>
public enum SwarmRole
{
    /// <summary>
    /// One per host. It subscribes to the coordinator, decides whether the host has room for another
    /// Worker, starts and watches the Worker processes, and ends them when the work is over.
    /// </summary>
    Hive = 1,

    /// <summary>
    /// One process doing the consuming application's work. It subscribes to the coordinator, runs
    /// until its work finishes or it is told to end, and exits with a documented code.
    /// </summary>
    Worker = 2
}
