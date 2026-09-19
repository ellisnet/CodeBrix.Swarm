using System;

namespace CodeBrix.Swarm.Core;

/// <summary>
/// The names the two roles travel under - inside an access token, as the claim minted from it, and
/// in the information string the token's key is derived from. They are spelled once, here, because a
/// role name that drifts between the coordinator and a client silently stops every token working.
/// </summary>
internal static class SwarmRoleNames
{
    /// <summary>The wire name of <see cref="SwarmRole.Hive" />.</summary>
    public const string Hive = "hive";

    /// <summary>The wire name of <see cref="SwarmRole.Worker" />.</summary>
    public const string Worker = "worker";

    /// <summary>Returns the wire name of the given role.</summary>
    public static string For(SwarmRole role)
    {
        return role switch
        {
            SwarmRole.Hive => Hive,
            SwarmRole.Worker => Worker,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown swarm role.")
        };
    }

    /// <summary>Reads a wire name back into a role. Comparison is exact and case-sensitive.</summary>
    public static bool TryParse(string name, out SwarmRole role)
    {
        switch (name)
        {
            case Hive:
                role = SwarmRole.Hive;
                return true;
            case Worker:
                role = SwarmRole.Worker;
                return true;
            default:
                role = default;
                return false;
        }
    }
}
