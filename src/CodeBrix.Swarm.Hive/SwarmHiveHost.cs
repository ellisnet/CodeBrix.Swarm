using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Diagnostics;
using CodeBrix.Swarm.Hive.HostLoad;
using CodeBrix.Swarm.Hive.Running;
using CodeBrix.Swarm.Hive.Spawning;

namespace CodeBrix.Swarm.Hive;

/// <summary>
/// Runs one Hive from start to finish: subscribe to the coordinator, watch the host, start Workers as
/// far as the host allows, hand each one its configuration, notice when they end, end them all when
/// the work is over, and say why it finished.
/// </summary>
/// <remarks>
/// <para>
/// A Hive's whole program is normally one line: run this, and exit on what it returns. Everything the
/// consuming application decides - what to start, how much of the host to leave alone, what to do
/// about a Worker that ended - it decides through the options.
/// </para>
/// <para>
/// ONE HIVE PER HOST is the arrangement this is built for. More than one on a host will work, but each
/// of them measures the whole host rather than its own share of it, so between them they can take more
/// of it than either was allowed. Give each one its own name and its own lower limits if it has to be
/// done.
/// </para>
/// </remarks>
public static class SwarmHiveHost
{
    /// <summary>
    /// Runs the Hive until the coordinator says the work is over, or stays out of reach for too long.
    /// </summary>
    /// <param name="options">Where the coordinator is, the tokens, and what to start.</param>
    /// <returns>Why the Hive finished.</returns>
    /// <exception cref="ArgumentNullException">The options are null.</exception>
    /// <exception cref="SwarmConfigurationException">Something in the options is missing or out of range.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// The host's operating system is not one a Hive knows how to measure.
    /// </exception>
    public static Task<SwarmHiveOutcome> RunAsync(SwarmHiveOptions options)
        => RunAsync(options, CancellationToken.None);

    /// <summary>
    /// Runs the Hive, with a way for the surrounding program to end it.
    /// </summary>
    /// <param name="options">Where the coordinator is, the tokens, and what to start.</param>
    /// <param name="cancellationToken">
    /// Cancelled to wind the Hive down from outside. Its Workers are ended in the same orderly way as
    /// they would be by the coordinator.
    /// </param>
    /// <returns>Why the Hive finished.</returns>
    /// <exception cref="ArgumentNullException">The options are null.</exception>
    /// <exception cref="SwarmConfigurationException">Something in the options is missing or out of range.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// The host's operating system is not one a Hive knows how to measure.
    /// </exception>
    public static Task<SwarmHiveOutcome> RunAsync(
        SwarmHiveOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunAsync(
            options,
            HostLoadProbes.ForThisHost(),
            WorkerProcessFactory.Instance,
            SignalRHubConnectionFactory.Instance,
            SystemSwarmClock.Instance,
            cancellationToken);
    }

    /// <summary>
    /// The exit code a Hive's own process should use for one of these endings. A Hive is a process
    /// like any other and something is usually watching what it exits with; these are the same codes
    /// a Worker uses for the same situations.
    /// </summary>
    /// <param name="outcome">Why the Hive finished.</param>
    /// <returns>One of the codes on <see cref="SwarmExitCodes" />.</returns>
    public static int ExitCodeFor(SwarmHiveOutcome outcome)
    {
        return outcome switch
        {
            SwarmHiveOutcome.QueenUnreachable => SwarmExitCodes.QueenUnreachable,
            SwarmHiveOutcome.Failed => SwarmExitCodes.WorkFailed,
            _ => SwarmExitCodes.Success
        };
    }

    /// <summary>
    /// Runs the Hive against a substituted host-load probe, process factory, connection factory and
    /// clock, so that the whole of its decision-making can be exercised without a host under load, a
    /// coordinator, or anything actually started.
    /// </summary>
    internal static async Task<SwarmHiveOutcome> RunAsync(
        SwarmHiveOptions options,
        IHostLoadProbe probe,
        IWorkerProcessFactory processes,
        ISwarmHubConnectionFactory connections,
        ISwarmClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(clock);

        options.Validate();

        return await RunAsync(
                options,
                ResolveLimits(options, Environment.GetEnvironmentVariable),
                ResolveHiveId(options),
                probe,
                processes,
                connections,
                clock,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the Hive with the limits and name already worked out, so that a test can hand over an
    /// environment of its own rather than the one the process really has.
    /// </summary>
    internal static Task<SwarmHiveOutcome> RunAsync(
        SwarmHiveOptions options,
        SwarmHostLimits limits,
        string hiveId,
        IHostLoadProbe probe,
        IWorkerProcessFactory processes,
        ISwarmHubConnectionFactory connections,
        ISwarmClock clock,
        CancellationToken cancellationToken)
    {
        var run = new HiveRun(options, limits, hiveId, probe, processes, clock);

        return run.RunAsync(connections, cancellationToken);
    }

    /// <summary>
    /// The limits this run uses: the ones the application asked for, lowered wherever the host's own
    /// environment asks for less.
    /// </summary>
    internal static SwarmHostLimits ResolveLimits(
        SwarmHiveOptions options,
        Func<string, string> readVariable)
    {
        var limits = HostLimitsFromEnvironment.Apply(options.Limits, readVariable);

        //The environment can only ever lower, so this cannot fail on account of anything it said -
        //but a limit that is about to be used is worth checking whatever produced it.
        limits.Validate();

        return limits;
    }

    /// <summary>
    /// The name this Hive runs under: the one it was given, or the host's own name.
    /// </summary>
    internal static string ResolveHiveId(SwarmHiveOptions options)
        => string.IsNullOrWhiteSpace(options.HiveId)
            ? Environment.MachineName
            : options.HiveId.Trim();
}
