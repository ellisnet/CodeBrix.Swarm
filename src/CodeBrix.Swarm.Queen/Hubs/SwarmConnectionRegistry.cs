using System;
using System.Collections.Concurrent;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Queen.Hubs;

/// <summary>
/// Keeps count of what is connected to each hub. The hubs have no methods a client can call, so this
/// is the only thing they do: record an arrival and a departure, and tell the consuming application
/// about it.
/// </summary>
internal sealed class SwarmConnectionRegistry
{
    private readonly ConcurrentDictionary<string, byte> _hives = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _workers = new(StringComparer.Ordinal);

    /// <summary>How many Hives are connected.</summary>
    public int HiveCount => _hives.Count;

    /// <summary>How many Workers are connected.</summary>
    public int WorkerCount => _workers.Count;

    /// <summary>Raised when something arrives at either hub.</summary>
    public event EventHandler<SwarmConnectionEventArgs> Connected;

    /// <summary>Raised when something leaves either hub.</summary>
    public event EventHandler<SwarmConnectionEventArgs> Disconnected;

    /// <summary>Records an arrival.</summary>
    public void Add(SwarmRole role, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        if (!Connections(role).TryAdd(connectionId, 0))
        {
            return;
        }

        Connected?.Invoke(this, new SwarmConnectionEventArgs(role, connectionId, HiveCount, WorkerCount));
    }

    /// <summary>Records a departure.</summary>
    public void Remove(SwarmRole role, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        if (!Connections(role).TryRemove(connectionId, out _))
        {
            return;
        }

        Disconnected?.Invoke(this, new SwarmConnectionEventArgs(role, connectionId, HiveCount, WorkerCount));
    }

    private ConcurrentDictionary<string, byte> Connections(SwarmRole role)
    {
        return role switch
        {
            SwarmRole.Hive => _hives,
            SwarmRole.Worker => _workers,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown swarm role.")
        };
    }
}
