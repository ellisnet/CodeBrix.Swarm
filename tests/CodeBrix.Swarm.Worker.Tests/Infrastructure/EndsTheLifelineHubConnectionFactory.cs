using System;
using CodeBrix.Swarm.Core.Connections;

namespace CodeBrix.Swarm.Worker.Tests.Infrastructure;

/// <summary>
/// Hands out <see cref="EndsTheLifelineHubConnection" /> instances, all of them ending the same lifeline.
/// </summary>
internal sealed class EndsTheLifelineHubConnectionFactory : ISwarmHubConnectionFactory
{
    private readonly Action _endTheLifeline;

    public EndsTheLifelineHubConnectionFactory(Action endTheLifeline)
        => _endTheLifeline = endTheLifeline;

    public ISwarmHubConnection Create(string hubUrl, string accessToken)
        => new EndsTheLifelineHubConnection(_endTheLifeline);
}
