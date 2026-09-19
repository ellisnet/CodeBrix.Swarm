namespace CodeBrix.Swarm.Hive;

/// <summary>
/// Why a Hive finished. A Hive runs until something ends it, and this says which of those things it
/// was. The consuming application reads it from what the Hive returns, and then exits.
/// </summary>
public enum SwarmHiveOutcome
{
    /// <summary>
    /// The coordinator sent the message that means the work is over. Every Worker was ended in an
    /// orderly way and nothing starts another one: there is no message that undoes this.
    /// </summary>
    ToldToTerminate = 1,

    /// <summary>
    /// The coordinator stayed out of reach for the whole retry window, at start-up or after losing
    /// the connection. The Workers that were running were ended in an orderly way first.
    /// </summary>
    QueenUnreachable = 2,

    /// <summary>
    /// Something outside the swarm ended the run - the cancellation token the Hive was given.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// The consuming application's own step - the one that is asked what to start next, or the one
    /// that sets the Hive up - ended by throwing.
    /// </summary>
    Failed = 4
}
