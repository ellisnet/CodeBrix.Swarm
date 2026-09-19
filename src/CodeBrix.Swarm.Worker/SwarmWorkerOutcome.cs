namespace CodeBrix.Swarm.Worker;

/// <summary>
/// Why a Worker stopped. The consuming application's shutdown step is given this, so it can tell an
/// ordinary end from one that was forced on it.
/// </summary>
public enum SwarmWorkerOutcome
{
    /// <summary>The work the application supplied ran to its end by itself.</summary>
    WorkFinished = 1,

    /// <summary>The coordinator sent the message that tells a Worker to end itself.</summary>
    ToldToTerminate = 2,

    /// <summary>
    /// The lifeline closed: the Hive that started this Worker has gone, or is asking it to exit.
    /// </summary>
    LifelineClosed = 3,

    /// <summary>
    /// The coordinator stayed out of reach for the whole retry window, at start-up or after a drop.
    /// </summary>
    QueenUnreachable = 4,

    /// <summary>The work the application supplied ended by throwing.</summary>
    WorkFailed = 5,

    /// <summary>
    /// The Worker never started: it could not read a usable configuration.
    /// </summary>
    ConfigurationInvalid = 6,

    /// <summary>
    /// Something outside the swarm cancelled the run - the cancellation token the host was given.
    /// </summary>
    Cancelled = 7
}
