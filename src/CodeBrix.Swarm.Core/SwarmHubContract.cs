using System;

namespace CodeBrix.Swarm.Core;

/// <summary>
/// Where the coordinator serves its two hubs, and the one method name it calls on everything
/// connected to them. Both ends of every connection read these constants from this one place: a
/// client that spells a path or a method name for itself is a client that drifts out of step with
/// the coordinator without anything failing to compile.
/// </summary>
public static class SwarmHubContract
{
    /// <summary>
    /// The path, relative to the coordinator's base address, of the hub that Hives connect to.
    /// </summary>
    public const string HiveHubPath = "/swarm/hive";

    /// <summary>
    /// The path, relative to the coordinator's base address, of the hub that Workers connect to.
    /// </summary>
    public const string WorkerHubPath = "/swarm/worker";

    /// <summary>
    /// The name of the single method the coordinator calls on a connected Hive or Worker, passing one
    /// <see cref="CodeBrix.Swarm.Core.Messaging.SwarmMessage" />. Traffic is one-way: neither hub
    /// exposes anything a client can call.
    /// </summary>
    public const string ReceiveMessageMethodName = "ReceiveSwarmMessage";

    /// <summary>
    /// Returns the hub path a process of the given role connects to.
    /// </summary>
    /// <param name="role">The role the process plays.</param>
    /// <returns><see cref="HiveHubPath" /> or <see cref="WorkerHubPath" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The role is not one of the known roles.</exception>
    public static string HubPathFor(SwarmRole role)
    {
        return role switch
        {
            SwarmRole.Hive => HiveHubPath,
            SwarmRole.Worker => WorkerHubPath,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown swarm role.")
        };
    }

    /// <summary>
    /// Joins a coordinator base address and the hub path for a role into the address a client
    /// connects to.
    /// </summary>
    /// <param name="baseUrl">The coordinator's base address, with or without a trailing slash.</param>
    /// <param name="role">The role the process plays.</param>
    /// <returns>The absolute address of that role's hub.</returns>
    /// <exception cref="ArgumentException">The base address is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The role is not one of the known roles.</exception>
    public static string HubUrlFor(string baseUrl, SwarmRole role)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("The coordinator base address must not be null or whitespace.", nameof(baseUrl));
        }

        return baseUrl.Trim().TrimEnd('/') + HubPathFor(role);
    }
}
