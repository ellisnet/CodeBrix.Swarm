using System;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Queen.Authentication;

/// <summary>
/// What was inside an access token once the coordinator has read it. Only the coordinator ever sees
/// this: a Hive or a Worker holds the token as an opaque string and cannot look inside it.
/// </summary>
public sealed class SwarmToken
{
    internal SwarmToken(Guid tokenId, SwarmRole role, DateTime issuedUtc, DateTime? expiresUtc)
    {
        TokenId = tokenId;
        Role = role;
        IssuedUtc = issuedUtc;
        ExpiresUtc = expiresUtc;
    }

    /// <summary>
    /// A different value in every token minted, so one can be told from another in the coordinator's
    /// own reporting.
    /// </summary>
    public Guid TokenId { get; }

    /// <summary>
    /// The role the token admits. It is inside the authenticated part of the token, and the token is
    /// also encrypted with a key derived for that role alone, so a token for one role is refused at
    /// the other role's hub twice over.
    /// </summary>
    public SwarmRole Role { get; }

    /// <summary>When the token was minted, in UTC.</summary>
    public DateTime IssuedUtc { get; }

    /// <summary>
    /// When the token stops being accepted, in UTC, or null for a token that does not expire.
    /// </summary>
    public DateTime? ExpiresUtc { get; }
}
