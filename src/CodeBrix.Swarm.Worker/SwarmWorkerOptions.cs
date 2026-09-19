using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Connections;

namespace CodeBrix.Swarm.Worker;

/// <summary>
/// What the consuming application hands a Worker: the work to do, what to do on the way out, and
/// how patient to be about the coordinator.
/// </summary>
public sealed class SwarmWorkerOptions
{
    /// <summary>
    /// The work this Worker exists to do. It is given the Worker's context and a token that is
    /// cancelled when the Worker is told to end, when its Hive goes away, or when the coordinator
    /// has been out of reach for too long. Returning normally means the work is finished.
    /// </summary>
    public Func<SwarmWorkerContext, CancellationToken, Task> WorkAsync { get; set; }

    /// <summary>
    /// Run after the configuration has been read and before the Worker connects. This is where to
    /// register handlers for application-defined messages, so that nothing sent in the first moments
    /// is missed. Optional.
    /// </summary>
    public Func<SwarmWorkerContext, CancellationToken, Task> ConfigureAsync { get; set; }

    /// <summary>
    /// Run once the work has ended, whatever ended it, and before the process exits. The context's
    /// outcome says why. It is given an uncancelled token, because this is the last chance to put
    /// things down tidily. Optional.
    /// </summary>
    public Func<SwarmWorkerContext, CancellationToken, Task> ShutdownAsync { get; set; }

    /// <summary>
    /// How long the Worker keeps trying to reach the coordinator before giving up - at start-up and
    /// again after losing the connection. While it is retrying after a drop, the work carries on.
    /// </summary>
    public TimeSpan QueenUnreachableWindow { get; set; } = SwarmConnectionOptions.DefaultQueenUnreachableWindow;

    /// <summary>
    /// How long to wait after the first attempt to reach the coordinator fails. Each further wait is
    /// twice the last, up to <see cref="MaximumRetryDelay" />.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The longest wait between attempts to reach the coordinator.</summary>
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Somewhere to put a line about anything the Worker dealt with by itself - a failed connection
    /// attempt, a handler that threw, the reason a configuration was refused. Optional; nothing is
    /// written anywhere when it is not set.
    /// </summary>
    public Action<string> Report { get; set; }

    /// <summary>
    /// Checks that everything needed is present and makes sense.
    /// </summary>
    /// <exception cref="SwarmConfigurationException">Something is missing or out of range.</exception>
    public void Validate()
    {
        if (WorkAsync == null)
        {
            throw new SwarmConfigurationException(
                "A Worker has no work to do: set WorkAsync to the work this Worker exists for.");
        }

        if (QueenUnreachableWindow < TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The window for reaching the coordinator must not be negative.");
        }

        if (FirstRetryDelay <= TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The first retry delay must be longer than nothing.");
        }

        if (MaximumRetryDelay < FirstRetryDelay)
        {
            throw new SwarmConfigurationException(
                "The longest retry delay must not be shorter than the first one.");
        }
    }
}
