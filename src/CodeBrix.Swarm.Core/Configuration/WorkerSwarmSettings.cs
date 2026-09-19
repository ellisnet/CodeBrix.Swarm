using System.Text.Json.Serialization;

namespace CodeBrix.Swarm.Core.Configuration;

/// <summary>
/// The swarm's own part of a Worker's configuration: everything the Worker needs to find the
/// coordinator and be let in, and the name it is known by. The Hive that starts the Worker fills
/// this in; the consuming application fills in the other part.
/// </summary>
public sealed class WorkerSwarmSettings
{
    /// <summary>
    /// The coordinator's base address, such as <c>http://192.168.1.10:5000</c>. It must be an
    /// absolute http or https address; the Worker appends the Worker hub's path to it itself.
    /// </summary>
    [JsonPropertyName("queenUrl")]
    public string QueenUrl { get; set; }

    /// <summary>
    /// The access token that lets this Worker into the coordinator's Worker hub. It is opaque: only
    /// the coordinator can read it, and it is no good at the Hive hub.
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; set; }

    /// <summary>
    /// What this Worker is called. The Hive gives every Worker it starts a different one, and it
    /// appears in the Worker's own reporting.
    /// </summary>
    [JsonPropertyName("workerId")]
    public string WorkerId { get; set; }
}
