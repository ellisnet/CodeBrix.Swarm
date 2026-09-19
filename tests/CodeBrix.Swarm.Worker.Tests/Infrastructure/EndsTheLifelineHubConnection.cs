using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Worker.Tests.Infrastructure;

/// <summary>
/// A connection that brings the Worker's lifeline to an end while it is still opening, and then waits to
/// be cancelled and says so - which is exactly what a real one does in that moment.
/// </summary>
/// <remarks>
/// There is a real instant for this to happen in. The coordinator accepts a connection and counts it
/// before the client's own start has finished, so a Hive that closes the lifeline the moment it sees the
/// Worker arrive cancels the connection attempt from underneath itself. A Worker must exit with the code
/// for the reason it was ended, not fall out of its own start-up.
/// </remarks>
internal sealed class EndsTheLifelineHubConnection : ISwarmHubConnection
{
    private readonly Action _endTheLifeline;

    public EndsTheLifelineHubConnection(Action endTheLifeline) => _endTheLifeline = endTheLifeline;

    /// <summary>
    /// Nothing ever raises this. A connection that never finishes opening never closes either.
    /// </summary>
    public event Func<Exception, Task> Closed
    {
        add { }
        remove { }
    }

    public void OnMessage(string methodName, Func<SwarmMessage, Task> handler)
    {
        //Nothing arrives on a connection that never opens.
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _endTheLifeline();

        //Waits for the Worker to notice that its lifeline has gone, which cancels this very attempt.
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
