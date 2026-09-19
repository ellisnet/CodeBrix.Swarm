using System;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Queen.Authentication;

/// <summary>
/// The names the coordinator's own authentication and authorization are wired up under. They never
/// leave this assembly: a Hive or a Worker only ever carries an opaque token.
/// </summary>
internal static class SwarmAuthorization
{
    /// <summary>The single authentication scheme both hubs use.</summary>
    public const string SchemeName = "CodeBrixSwarmToken";

    /// <summary>The policy the Hive hub is guarded by.</summary>
    public const string HivePolicy = "CodeBrixSwarmHivePolicy";

    /// <summary>The policy the Worker hub is guarded by.</summary>
    public const string WorkerPolicy = "CodeBrixSwarmWorkerPolicy";

    /// <summary>
    /// The claim the role inside a token becomes, and the claim each hub's policy insists on. A
    /// token is only decryptable with its own role's key, and this is the second check on top of
    /// that: the role travels inside the authenticated part of the token, so it cannot be altered.
    /// </summary>
    public const string RoleClaimType = "codebrix-swarm-role";

    /// <summary>Returns the policy name that guards the given role's hub.</summary>
    public static string PolicyFor(SwarmRole role)
    {
        return role switch
        {
            SwarmRole.Hive => HivePolicy,
            SwarmRole.Worker => WorkerPolicy,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown swarm role.")
        };
    }
}
