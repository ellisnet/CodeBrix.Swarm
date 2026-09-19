using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Worker.Tests.Infrastructure;

/// <summary>
/// A hub connection that does nothing but remember what was asked of it, can be told to refuse to
/// open, and can be made to deliver a message or to drop on demand.
/// </summary>
internal sealed class FakeHubConnection : ISwarmHubConnection
{
    private Func<SwarmMessage, Task> _handler;

    public FakeHubConnection(string hubUrl, string accessToken)
    {
        HubUrl = hubUrl;
        AccessToken = accessToken;
    }

    public event Func<Exception, Task> Closed;

    public string HubUrl { get; }

    public string AccessToken { get; }

    public string RegisteredMethodName { get; private set; }

    public bool FailOnStart { get; set; }

    public bool IsStarted { get; private set; }

    public bool IsStopped { get; private set; }

    public bool IsDisposed { get; private set; }

    public void OnMessage(string methodName, Func<SwarmMessage, Task> handler)
    {
        RegisteredMethodName = methodName;
        _handler = handler;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailOnStart)
        {
            throw new InvalidOperationException("This connection was told to refuse to open.");
        }

        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        IsStopped = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    public Task DeliverAsync(SwarmMessage message)
    {
        var handler = _handler;
        return handler == null ? Task.CompletedTask : handler(message);
    }

    public Task DropAsync(Exception error)
    {
        var closed = Closed;
        return closed == null ? Task.CompletedTask : closed(error);
    }
}
