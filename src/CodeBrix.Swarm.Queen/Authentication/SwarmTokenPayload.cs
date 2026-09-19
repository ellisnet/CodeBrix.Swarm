using System.Text.Json.Serialization;

namespace CodeBrix.Swarm.Queen.Authentication;

/// <summary>
/// What is actually encrypted inside an access token. The names are short because every byte of this
/// is encrypted, carried in an HTTP header and repeated on a query string.
/// </summary>
internal sealed class SwarmTokenPayload
{
    /// <summary>The token's own identifier.</summary>
    [JsonPropertyName("jti")]
    public string TokenId { get; set; }

    /// <summary>The role this token admits, as its wire name.</summary>
    [JsonPropertyName("rol")]
    public string Role { get; set; }

    /// <summary>When the token was minted, as seconds since the Unix epoch.</summary>
    [JsonPropertyName("iat")]
    public long IssuedAtUnixSeconds { get; set; }

    /// <summary>
    /// When the token stops being accepted, as seconds since the Unix epoch, or absent for a token
    /// that does not expire.
    /// </summary>
    [JsonPropertyName("exp")]
    public long? ExpiresAtUnixSeconds { get; set; }
}
