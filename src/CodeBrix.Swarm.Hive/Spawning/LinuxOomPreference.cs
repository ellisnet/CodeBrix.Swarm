using System;
using System.Globalization;
using System.IO;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// Asks the Linux kernel to pick a Worker before anything else if the host ever runs out of memory
/// altogether.
/// </summary>
/// <remarks>
/// <para>
/// A Hive does everything it can to keep a host from reaching that state, but the moment can arrive
/// anyway - something outside the swarm takes a great deal of memory at once, and the kernel has to
/// end SOMETHING. Left to itself it tends to choose the largest process, which on a host running a
/// swarm may well be the Hive itself, or whatever the host is really there for. A Worker is the right
/// thing to lose: it is one unit of work, its coordinator will notice, and another one can be started
/// when there is room.
/// </para>
/// <para>
/// The kernel takes this preference per process, in a file it publishes for each one. A process may
/// always make ITSELF and its own children MORE likely to be chosen; making anything less likely
/// needs privileges the swarm does not ask for. So this only ever raises the figure, and the whole
/// thing is best effort: a host that does not allow it carries on exactly as before.
/// </para>
/// </remarks>
internal static class LinuxOomPreference
{
    /// <summary>
    /// The figure a Worker is marked with. The kernel's range runs from a thousand below zero, meaning
    /// never choose this, to a thousand above, meaning choose this first. Halfway up puts a Worker
    /// well ahead of everything ordinary without making it the certain choice over something that has
    /// asked to be even more expendable.
    /// </summary>
    public const int PreferredVictimScore = 500;

    /// <summary>
    /// Marks one process as a preferred choice, if the host allows it.
    /// </summary>
    /// <param name="processId">The process to mark.</param>
    /// <returns>True when the mark was written.</returns>
    public static bool TryPrefer(int processId) => TryPrefer(processId, PreferredVictimScore);

    /// <summary>
    /// Marks one process with a particular figure, if the host allows it.
    /// </summary>
    /// <param name="processId">The process to mark.</param>
    /// <param name="score">The figure to write.</param>
    /// <returns>True when the mark was written.</returns>
    public static bool TryPrefer(int processId, int score)
    {
        if (!OperatingSystem.IsLinux() || processId <= 0)
        {
            return false;
        }

        try
        {
            File.WriteAllText(
                PathFor(processId),
                score.ToString(CultureInfo.InvariantCulture));

            return true;
        }
        catch (Exception)
        {
            //A host that does not allow it, or a Worker that has already ended. Neither is worth
            //stopping for.
            return false;
        }
    }

    /// <summary>
    /// Where the kernel keeps the preference for one process.
    /// </summary>
    /// <param name="processId">The process.</param>
    /// <returns>The path of the file.</returns>
    public static string PathFor(int processId)
        => "/proc/" + processId.ToString(CultureInfo.InvariantCulture) + "/oom_score_adj";
}
