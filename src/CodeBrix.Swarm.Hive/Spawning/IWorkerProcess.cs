using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// One Worker process, as far as the Hive that started it is concerned. A test puts a substitute here
/// so that the whole of a Hive's decision-making can be exercised without starting anything.
/// </summary>
internal interface IWorkerProcess : IDisposable
{
    /// <summary>What this Worker is called.</summary>
    string WorkerId { get; }

    /// <summary>The operating system's number for the process.</summary>
    int ProcessId { get; }

    /// <summary>When the process was started, in UTC.</summary>
    DateTime StartedUtc { get; }

    /// <summary>True once the process has ended.</summary>
    bool HasExited { get; }

    /// <summary>
    /// The code the process ended with. It is only meaningful once <see cref="HasExited" /> is true.
    /// </summary>
    int ExitCode { get; }

    /// <summary>
    /// How much memory the process is using at this moment, in bytes, or zero when that cannot be
    /// read. A Hive averages this across its Workers to guess what the next one will take.
    /// </summary>
    long WorkingSetBytes { get; }

    /// <summary>
    /// Closes the lifeline - the pipe the Worker's configuration arrived on, which has stayed open
    /// ever since. The Worker reads the end of it as "your Hive has gone, or is asking you to exit",
    /// and winds itself down.
    /// </summary>
    void CloseLifeline();

    /// <summary>
    /// Stops the process and everything it started, without asking. This is what happens to whatever
    /// is left when the grace period after closing the lifelines has run out.
    /// </summary>
    void ForceStop();

    /// <summary>
    /// Waits for the process to end.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to stop waiting.</param>
    /// <returns>A task that completes once the process has ended.</returns>
    Task WaitForExitAsync(CancellationToken cancellationToken);
}
