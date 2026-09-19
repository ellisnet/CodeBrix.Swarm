using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Messaging;
using Microsoft.AspNetCore.SignalR.Client;

namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// The real hub connection: the SignalR client, carrying the access token the way the coordinator's
/// authentication expects it - as a bearer token on the HTTP request, and as the access_token query
/// value on the socket, which is the only place a browser-style socket handshake can put it.
/// </summary>
/// <remarks>
/// The SignalR client's own automatic reconnection is deliberately NOT turned on. The swarm has its
/// own rule about how long a coordinator may stay out of reach, and two reconnection policies
/// running at once would make the one that matters unobservable.
/// </remarks>
internal sealed class SignalRHubConnection : ISwarmHubConnection
{
    private readonly HubConnection _connection;

    public SignalRHubConnection(string hubUrl, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(hubUrl))
        {
            throw new ArgumentException("The hub address must not be null or whitespace.", nameof(hubUrl));
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .Build();
    }

    public event Func<Exception, Task> Closed
    {
        add => _connection.Closed += value;
        remove => _connection.Closed -= value;
    }

    public void OnMessage(string methodName, Func<SwarmMessage, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("The method name must not be null or whitespace.", nameof(methodName));
        }

        _connection.On(methodName, handler);
    }

    public Task StartAsync(CancellationToken cancellationToken) => _connection.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _connection.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
