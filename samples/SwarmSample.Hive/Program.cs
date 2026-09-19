using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Hive;

namespace SwarmSample.Hive;

/// <summary>
/// The smallest honest Hive: one per host, it subscribes to the coordinator, starts Workers for as
/// long as the host has room, and finishes when the coordinator says the work is over.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here measures the host, starts a process, writes a configuration to one, watches for it to
/// end, waits after one that failed, or ends the rest when the time comes. All of that is the
/// library's. What is left - and it is all a consuming application ever has to write - is the answer
/// to one question, asked over and over: what do I start next?
/// </para>
/// <para>
/// The answer here is "another Worker, with a little piece of JSON describing what it should count
/// to", and, once enough have been handed out, "not now" - which is not a failure and never counts
/// against anything.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The kind of message this Hive listens for besides the swarm's own.
    /// </summary>
    private const string NoteMessageKind = "sample.note";

    private static async Task<int> Main(string[] args)
    {
        using var stopping = new CancellationTokenSource();

        Console.CancelKeyPress += (_, keyPress) =>
        {
            //Ending the Hive tidily is better than having the runtime end the process: the Workers on
            //this host would otherwise be left to notice for themselves.
            keyPress.Cancel = true;
            stopping.Cancel();
        };

        HiveSettings settings;

        try
        {
            settings = await HiveSettings.ReadAsync(args, stopping.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("The settings could not be read: " + ex.Message);
            return SwarmExitCodes.ConfigurationInvalid;
        }

        if (settings == null)
        {
            Console.Error.WriteLine(
                "This Hive reads one line of JSON on standard input, or from the file named with "
                + HiveSettings.FileSwitch + "<path>. The sample coordinator prints a line ready to "
                + "use.");
            return SwarmExitCodes.ConfigurationInvalid;
        }

        var problem = settings.FirstProblem();

        if (problem != null)
        {
            Console.Error.WriteLine("The settings cannot be used: " + problem);
            return SwarmExitCodes.ConfigurationInvalid;
        }

        void Write(string line)
        {
            if (settings.Report)
            {
                Console.Out.WriteLine(line);
                Console.Out.Flush();
            }
        }

        var options = new SwarmHiveOptions
        {
            QueenUrl = settings.QueenUrl,
            HiveToken = settings.HiveToken,
            WorkerToken = settings.WorkerToken,
            HiveId = settings.HiveId,
            Report = Write,
            WorkerExited = exit => Write(
                "worker ended: " + exit.WorkerId + " code "
                + exit.ExitCode.ToString(CultureInfo.InvariantCulture)),

            //Runs before the Hive connects, so that a message sent in the first moments is not missed.
            ConfigureAsync = (context, _) =>
            {
                context.RegisterHandler(NoteMessageKind, (message, _) =>
                {
                    Write("note " + message.Payload);
                    return Task.CompletedTask;
                });

                return Task.CompletedTask;
            },

            //THE ONE QUESTION AN APPLICATION HAS TO ANSWER.
            NextWorkerAsync = (context, _) =>
            {
                if (settings.TotalWorkers > 0 && context.StartedWorkerCount >= settings.TotalWorkers)
                {
                    return Task.FromResult(WorkerLaunch.NotNow);
                }

                var plan = new
                {
                    label = context.HiveId,
                    steps = settings.WorkerSteps,
                    stepMilliseconds = settings.WorkerStepMilliseconds,
                    report = settings.Report
                };

                return Task.FromResult(WorkerLaunch.StartWithWork(
                    settings.WorkerExecutable,
                    settings.WorkerArguments(),
                    plan));
            },

            ShutdownAsync = (context, _) =>
            {
                Write("finishing because " + context.Outcome);
                return Task.CompletedTask;
            }
        };

        if (settings.MaxWorkers.HasValue)
        {
            options.Limits.MaxWorkers = settings.MaxWorkers;
        }

        SwarmHiveOutcome outcome;

        try
        {
            outcome = await SwarmHiveHost.RunAsync(options, stopping.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("The Hive could not start: " + ex.Message);
            return SwarmExitCodes.ConfigurationInvalid;
        }

        Write("finished: " + outcome);

        return SwarmHiveHost.ExitCodeFor(outcome);
    }
}
