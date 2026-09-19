namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// Makes real SignalR hub connections. This is what a Hive or a Worker uses unless something has
/// deliberately put a substitute in its place.
/// </summary>
internal sealed class SignalRHubConnectionFactory : ISwarmHubConnectionFactory
{
    /// <summary>
    /// The one instance. The factory holds no state.
    /// </summary>
    public static readonly SignalRHubConnectionFactory Instance = new();

    private SignalRHubConnectionFactory() { }

    /// <inheritdoc />
    public ISwarmHubConnection Create(string hubUrl, string accessToken)
        => new SignalRHubConnection(hubUrl, accessToken);
}
