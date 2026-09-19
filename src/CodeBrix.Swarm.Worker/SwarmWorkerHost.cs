using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Diagnostics;
using CodeBrix.Swarm.Worker.Startup;

namespace CodeBrix.Swarm.Worker;

/// <summary>
/// Runs one Worker from start to finish: read the configuration, subscribe to the coordinator, do
/// the consuming application's work until something ends it, run the application's shutdown step,
/// and return the exit code that says what happened.
/// </summary>
/// <remarks>
/// A Worker's whole program is normally one line: return what this returns.
/// </remarks>
public static class SwarmWorkerHost
{
    /// <summary>
    /// Runs the Worker.
    /// </summary>
    /// <param name="options">The work to do, and how patient to be about the coordinator.</param>
    /// <param name="args">The process's command-line arguments, without the program name.</param>
    /// <returns>One of the codes on <see cref="SwarmExitCodes" />.</returns>
    public static Task<int> RunAsync(SwarmWorkerOptions options, string[] args)
        => RunAsync(options, args, CancellationToken.None);

    /// <summary>
    /// Runs the Worker, with a way for the surrounding program to end it.
    /// </summary>
    /// <param name="options">The work to do, and how patient to be about the coordinator.</param>
    /// <param name="args">The process's command-line arguments, without the program name.</param>
    /// <param name="cancellationToken">Cancelled to wind the Worker down from outside.</param>
    /// <returns>One of the codes on <see cref="SwarmExitCodes" />.</returns>
    public static Task<int> RunAsync(
        SwarmWorkerOptions options,
        string[] args,
        CancellationToken cancellationToken)
        => RunAsync(
            options,
            args,
            Console.In,
            SignalRHubConnectionFactory.Instance,
            SystemSwarmClock.Instance,
            cancellationToken);

    /// <summary>
    /// Runs the Worker against a substituted standard input, connection factory and clock, so that
    /// the whole sequence can be exercised without a coordinator or a Hive.
    /// </summary>
    internal static async Task<int> RunAsync(
        SwarmWorkerOptions options,
        string[] args,
        TextReader standardInput,
        ISwarmHubConnectionFactory connectionFactory,
        ISwarmClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        WorkerStartup startup;

        try
        {
            startup = await WorkerConfigurationReader
                .ReadAsync(args, standardInput, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SwarmExitCodes.Success;
        }
        catch (SwarmConfigurationException ex)
        {
            Report(options, ex.Message);
            return SwarmExitCodes.ConfigurationInvalid;
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ending = new WorkerEnding(stopping);

        using var externalRegistration = cancellationToken.Register(
            () => ending.Record(SwarmWorkerOutcome.Cancelled));

        var connectionOptions = new SwarmConnectionOptions
        {
            QueenUrl = startup.Configuration.Swarm.QueenUrl,
            Token = startup.Configuration.Swarm.Token,
            QueenUnreachableWindow = options.QueenUnreachableWindow,
            FirstRetryDelay = options.FirstRetryDelay,
            MaximumRetryDelay = options.MaximumRetryDelay
        };

        await using var client = new SwarmHubClient(
            SwarmRole.Worker, connectionOptions, connectionFactory, clock);

        var context = new SwarmWorkerContext(
            startup.Configuration.Swarm.WorkerId,
            startup.Configuration.Work,
            startup.IsDevelopmentMode,
            client.Messages);

        client.OnTerminate(_ =>
        {
            ending.Request(SwarmWorkerOutcome.ToldToTerminate);
            return Task.CompletedTask;
        });

        client.QueenUnreachable += (_, _) => ending.Request(SwarmWorkerOutcome.QueenUnreachable);
        client.Error += (_, error) => Report(options, DescribeError(error));

        WorkerLifeline lifeline = null;

        //A Worker started by hand from a configuration file has no Hive, so there is no lifeline to
        //watch and nothing on standard input to wait for.
        if (!startup.IsDevelopmentMode)
        {
            lifeline = new WorkerLifeline(standardInput);
            lifeline.Closed += (_, _) => ending.Request(SwarmWorkerOutcome.LifelineClosed);
            lifeline.Start();
        }

        try
        {
            if (options.ConfigureAsync != null)
            {
                try
                {
                    await options.ConfigureAsync(context, stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    //Something ended the Worker before it had finished setting itself up.
                }
                catch (Exception ex)
                {
                    ending.Record(SwarmWorkerOutcome.WorkFailed);
                    Report(options, "The Worker's configure step threw: " + ex.Message);
                }
            }

            if (!ending.IsRequested)
            {
                try
                {
                    var connected = await client.StartAsync(stopping.Token).ConfigureAwait(false);

                    if (!connected)
                    {
                        ending.Record(SwarmWorkerOutcome.QueenUnreachable);
                    }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    //SOMETHING ENDED THE WORKER WHILE IT WAS STILL CONNECTING. There is a real moment
                    //for this to happen in: the coordinator accepts the connection and counts it, and
                    //the client's own start has not quite finished. A Hive that closes the lifeline in
                    //that moment, or a terminate message that arrives in it, cancels the connection
                    //attempt from underneath itself.
                    //
                    //The reason is already recorded, and the Worker must exit with the code for that
                    //reason - not fall out of its own start-up, which would end the process with
                    //whatever the runtime does about an unhandled failure and tell the Hive that
                    //watched it something completely untrue.
                    Report(options, "The Worker was ended while it was still connecting.");
                }
            }

            if (ShouldRunWork(ending))
            {
                try
                {
                    await options.WorkAsync(context, stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    //Something ended the Worker while the work was running; the reason is already
                    //recorded, and stopping in answer to it is not a failure.
                }
                catch (Exception ex)
                {
                    ending.Record(SwarmWorkerOutcome.WorkFailed);
                    Report(options, "The Worker's work threw: " + ex.Message);
                }
            }

            context.Outcome = ending.Outcome;

            if (options.ShutdownAsync != null)
            {
                try
                {
                    await options.ShutdownAsync(context, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ending.Record(SwarmWorkerOutcome.WorkFailed);
                    context.Outcome = ending.Outcome;
                    Report(options, "The Worker's shutdown step threw: " + ex.Message);
                }
            }

            await client.StopAsync(CancellationToken.None).ConfigureAwait(false);

            return ExitCodeFor(ending.Outcome);
        }
        finally
        {
            lifeline?.Dispose();
        }
    }

    private static bool ShouldRunWork(WorkerEnding ending)
    {
        var outcome = ending.Outcome;

        return outcome == SwarmWorkerOutcome.WorkFinished
               && !ending.IsRequested;
    }

    private static int ExitCodeFor(SwarmWorkerOutcome outcome)
    {
        return outcome switch
        {
            SwarmWorkerOutcome.QueenUnreachable => SwarmExitCodes.QueenUnreachable,
            SwarmWorkerOutcome.WorkFailed => SwarmExitCodes.WorkFailed,
            SwarmWorkerOutcome.ConfigurationInvalid => SwarmExitCodes.ConfigurationInvalid,
            _ => SwarmExitCodes.Success
        };
    }

    private static string DescribeError(SwarmConnectionErrorEventArgs error)
    {
        if (error == null)
        {
            return "The Worker's connection reported something with no detail.";
        }

        return error.Error == null
            ? $"The Worker's connection, while {error.Context}: the connection closed."
            : $"The Worker's connection, while {error.Context}: {error.Error.Message}";
    }

    private static void Report(SwarmWorkerOptions options, string message)
    {
        var report = options.Report;

        if (report == null)
        {
            return;
        }

        try
        {
            report(message);
        }
        catch (Exception)
        {
            //Reporting is best effort; a reporter that throws must not end the Worker.
        }
    }
}
