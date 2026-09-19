using System;
using System.Collections.Generic;
using CodeBrix.Swarm.Core.Connections;

namespace CodeBrix.Swarm.Worker.Tests.Infrastructure;

/// <summary>
/// Hands out <see cref="FakeHubConnection" /> instances and keeps every one it made, so a test can
/// count the attempts and reach the connection that is currently live. A test decides which attempts
/// fail by their position in the sequence.
/// </summary>
internal sealed class FakeHubConnectionFactory : ISwarmHubConnectionFactory
{
    private readonly Func<int, bool> _failAttempt;
    private readonly List<FakeHubConnection> _created = [];
    private readonly object _gate = new();

    public FakeHubConnectionFactory()
        : this(_ => false) { }

    public FakeHubConnectionFactory(Func<int, bool> failAttempt)
        => _failAttempt = failAttempt ?? (_ => false);

    public IReadOnlyList<FakeHubConnection> Created
    {
        get
        {
            lock (_gate)
            {
                return _created.ToArray();
            }
        }
    }

    public FakeHubConnection Latest
    {
        get
        {
            lock (_gate)
            {
                return _created.Count == 0 ? null : _created[^1];
            }
        }
    }

    public ISwarmHubConnection Create(string hubUrl, string accessToken)
    {
        lock (_gate)
        {
            var connection = new FakeHubConnection(hubUrl, accessToken)
            {
                FailOnStart = _failAttempt(_created.Count)
            };

            _created.Add(connection);
            return connection;
        }
    }
}
