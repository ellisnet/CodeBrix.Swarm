namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// Makes a hub connection. Every attempt gets a fresh one, because a connection that failed to open
/// cannot be opened again.
/// </summary>
internal interface ISwarmHubConnectionFactory
{
    /// <summary>
    /// Creates a connection to one hub, carrying one access token.
    /// </summary>
    /// <param name="hubUrl">The absolute address of the hub.</param>
    /// <param name="accessToken">The token to present.</param>
    /// <returns>A connection that has not been opened yet.</returns>
    ISwarmHubConnection Create(string hubUrl, string accessToken);
}
