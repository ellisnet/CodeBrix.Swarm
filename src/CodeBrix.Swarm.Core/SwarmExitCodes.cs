namespace CodeBrix.Swarm.Core;

/// <summary>
/// The codes a Worker process exits with, and that a Hive's own process uses for the same situations.
/// They are part of the contract between a Worker and the Hive that started it: the Hive reads the
/// code to tell an ordinary end from a failure, and waits longer before starting another Worker when
/// it sees a failure straight after a start. The values follow the long-standing conventional
/// meanings, so they do not collide with the codes a shell produces for a process that was stopped
/// from outside.
/// </summary>
public static class SwarmExitCodes
{
    /// <summary>
    /// The Worker ended normally: its work finished, or it was told to end, or the Hive that started
    /// it closed the lifeline. This is never treated as a failure.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// The Worker could not read a usable configuration: nothing arrived on standard input, the line
    /// was not JSON, a required field was missing, or the file named on the command line could not be
    /// read.
    /// </summary>
    public const int ConfigurationInvalid = 78;

    /// <summary>
    /// The Worker could not reach the coordinator for the whole of its retry window, either at
    /// start-up or after losing the connection.
    /// </summary>
    public const int QueenUnreachable = 69;

    /// <summary>
    /// The work the consuming application supplied ended by throwing.
    /// </summary>
    public const int WorkFailed = 70;

    /// <summary>
    /// Returns true when the code means the Worker ended without a failure.
    /// </summary>
    /// <param name="exitCode">The code the process exited with.</param>
    /// <returns>True for <see cref="Success" />; false for anything else.</returns>
    public static bool IsSuccess(int exitCode) => exitCode == Success;
}
