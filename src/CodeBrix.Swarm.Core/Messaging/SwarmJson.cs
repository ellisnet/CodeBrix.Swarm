using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Swarm.Core.Messaging;

/// <summary>
/// The one set of JSON options the swarm uses for message payloads and for the Worker configuration.
/// Both ends read and write through it, so a payload written by the coordinator is understood by a
/// Hive or a Worker without either side agreeing on anything else.
/// </summary>
internal static class SwarmJson
{
    /// <summary>
    /// Compact output, camel-cased property names - matching what the hub protocol itself does - and
    /// no null properties written.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
