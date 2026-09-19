using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Core.Diagnostics;

/// <summary>
/// The only way anything in the swarm reads the time or waits. Every retry window, settle interval
/// and backoff goes through this, so a test can run a sixty-second rule to its end in no time at all
/// by substituting a clock that jumps forward instead of sleeping.
/// </summary>
internal interface ISwarmClock
{
    /// <summary>The current time, in UTC.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Waits for the given span.
    /// </summary>
    /// <param name="delay">How long to wait. A span of zero or less returns immediately.</param>
    /// <param name="cancellationToken">Cancelled when the wait should be given up on.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
