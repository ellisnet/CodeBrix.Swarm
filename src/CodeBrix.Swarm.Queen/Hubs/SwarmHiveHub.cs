using System;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Queen.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CodeBrix.Swarm.Queen.Hubs;

/// <summary>
/// The hub every Hive connects to. It has NO methods a client can call, on purpose: traffic in a
/// swarm is one-way, and an authenticated Hive can do nothing to the coordinator but listen. All it
/// does is note who has arrived and who has left.
/// </summary>
[Authorize(SwarmAuthorization.HivePolicy)]
internal sealed class SwarmHiveHub : Hub
{
    private readonly SwarmConnectionRegistry _registry;

    public SwarmHiveHub(SwarmConnectionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public override Task OnConnectedAsync()
    {
        _registry.Add(SwarmRole.Hive, Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        _registry.Remove(SwarmRole.Hive, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
