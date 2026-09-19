using System;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Queen;

/// <summary>
/// A Hive or a Worker has arrived at, or left, one of the coordinator's hubs. The counts are the
/// ones that were true the moment the change was recorded.
/// </summary>
public sealed class SwarmConnectionEventArgs : EventArgs
{
    internal SwarmConnectionEventArgs(SwarmRole role, string connectionId, int hiveCount, int workerCount)
    {
        Role = role;
        ConnectionId = connectionId;
        HiveCount = hiveCount;
        WorkerCount = workerCount;
    }

    /// <summary>Which hub the change happened at.</summary>
    public SwarmRole Role { get; }

    /// <summary>
    /// The identifier the hub gave this connection. It is a new value every time something connects,
    /// even when the same process reconnects.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>How many Hives were connected just after the change.</summary>
    public int HiveCount { get; }

    /// <summary>How many Workers were connected just after the change.</summary>
    public int WorkerCount { get; }
}
