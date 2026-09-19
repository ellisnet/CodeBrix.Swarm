using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Core.Diagnostics;

/// <summary>
/// The clock everything in the swarm uses when it is really running: the machine's own UTC time and
/// an ordinary asynchronous wait.
/// </summary>
internal sealed class SystemSwarmClock : ISwarmClock
{
    /// <summary>
    /// The one instance. The clock holds no state, so there is no reason for a second.
    /// </summary>
    public static readonly SystemSwarmClock Instance = new();

    private SystemSwarmClock() { }

    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;

    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return Task.Delay(delay, cancellationToken);
    }
}
