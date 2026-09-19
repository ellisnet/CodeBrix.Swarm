using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Worker;

namespace SwarmSample.Worker;

/// <summary>
/// The smallest honest Worker: it reads the configuration its Hive wrote to its standard input,
/// subscribes to the coordinator, counts for a while, and exits.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here parses standard input, watches for the end of the pipe, connects to anything, or
/// decides what to exit with. All of that is the library's, and the whole program is: describe the
/// work, hand the description over, exit on what comes back.
/// </para>
/// <para>
/// It can also be started by hand, with no Hive anywhere, by putting the same JSON in a file and
/// naming it with <c>--swarm-config &lt;path&gt;</c>. That is how a Worker is debugged, and that one
/// switch is the only thing the library reads from a command line.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The kind of message this Worker listens for besides the swarm's own. A coordinator sends one of
    /// these to every Worker at once.
    /// </summary>
    private const string NoteMessageKind = "sample.note";

    /// <summary>
    /// An optional switch of this sample's own that shortens how long this Worker waits for a
    /// coordinator that is not answering. It is here to show that an application's command line is its
    /// own: the library reads nothing from it but <c>--swarm-config</c>.
    /// </summary>
    private const string QueenWindowSwitch = "--queen-window-seconds=";

    private static async Task<int> Main(string[] args)
    {
        var plan = new WorkPlan();
        var verbose = false;

        void Write(string line)
        {
            if (!verbose)
            {
                return;
            }

            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }

        var options = new SwarmWorkerOptions
        {
            Report = message => Write("report: " + message),

            //Runs before the Worker connects, so that a message sent in the first moments is not
            //missed.
            ConfigureAsync = (context, _) =>
            {
                plan = context.GetWork<WorkPlan>() ?? new WorkPlan();
                verbose = plan.Report;

                Write("workerId " + context.WorkerId);
                Write("work " + context.Work);
                Write("developmentMode " + context.IsDevelopmentMode.ToString().ToLowerInvariant());

                context.RegisterHandler(NoteMessageKind, (message, _) =>
                {
                    Write("note " + message.Payload);
                    return Task.CompletedTask;
                });

                return Task.CompletedTask;
            },

            //The work. Returning normally means it is finished; throwing means it failed; the token is
            //cancelled when the coordinator says to end, when the Hive goes, or when the coordinator
            //has been out of reach too long.
            WorkAsync = async (_, cancellationToken) =>
            {
                var step = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (plan.FailAtStep >= 0 && step == plan.FailAtStep)
                    {
                        throw new InvalidOperationException(
                            "The sample work was told to fail at step "
                            + step.ToString(CultureInfo.InvariantCulture) + ".");
                    }

                    if (plan.Steps > 0 && step >= plan.Steps)
                    {
                        break;
                    }

                    step++;
                    Write("step " + step.ToString(CultureInfo.InvariantCulture) + " of " + plan.Label);

                    if (plan.StepMilliseconds > 0)
                    {
                        await Task.Delay(plan.StepMilliseconds, cancellationToken).ConfigureAwait(false);
                    }
                }

                Write("finished after " + step.ToString(CultureInfo.InvariantCulture) + " step(s)");
            },

            //Runs whatever ended the work, with an uncancelled token, so there is always somewhere to
            //put things down tidily.
            ShutdownAsync = (context, _) =>
            {
                Write("ending because " + context.Outcome);
                return Task.CompletedTask;
            }
        };

        if (TryReadQueenWindowSeconds(args, out var seconds))
        {
            options.QueenUnreachableWindow = TimeSpan.FromSeconds(seconds);
            options.FirstRetryDelay = TimeSpan.FromMilliseconds(100);
            options.MaximumRetryDelay = TimeSpan.FromMilliseconds(250);
        }

        return await SwarmWorkerHost.RunAsync(options, args, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool TryReadQueenWindowSeconds(string[] args, out double seconds)
    {
        seconds = 0d;

        if (args == null)
        {
            return false;
        }

        foreach (var argument in args)
        {
            if (argument == null || !argument.StartsWith(QueenWindowSwitch, StringComparison.Ordinal))
            {
                continue;
            }

            var value = argument[QueenWindowSwitch.Length..];

            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                && parsed > 0d)
            {
                seconds = parsed;
                return true;
            }
        }

        return false;
    }
}
