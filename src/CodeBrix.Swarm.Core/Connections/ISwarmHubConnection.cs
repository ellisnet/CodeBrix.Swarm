using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// The little that the swarm's connection logic needs from a hub connection. The real implementation
/// wraps the SignalR client; a test substitutes one that fails on demand, so the retry window and
/// everything built on it can be exercised without a coordinator.
/// </summary>
internal interface ISwarmHubConnection : IAsyncDisposable
{
    /// <summary>
    /// Raised when the connection goes away, with the failure that ended it or null for an orderly
    /// close.
    /// </summary>
    event Func<Exception, Task> Closed;

    /// <summary>
    /// Registers what to do when the coordinator calls the named method. Called once, before
    /// <see cref="StartAsync" />.
    /// </summary>
    /// <param name="methodName">The method the coordinator calls.</param>
    /// <param name="handler">What to do with the message it passes.</param>
    void OnMessage(string methodName, Func<SwarmMessage, Task> handler);

    /// <summary>Opens the connection.</summary>
    /// <param name="cancellationToken">Cancelled to give up on the attempt.</param>
    /// <returns>A task that completes once the connection is open.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection in an orderly way.</summary>
    /// <param name="cancellationToken">Cancelled to stop waiting for the close.</param>
    /// <returns>A task that completes once the connection is closed.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}
