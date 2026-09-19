using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.Queen;

namespace SwarmSample.Queen;

/// <summary>
/// The smallest honest coordinator: it opens the two hubs, mints the two access tokens a host needs,
/// prints them, and then sends messages when it is told to.
/// </summary>
/// <remarks>
/// <para>
/// The library owns the web host, so this program is not a web application and does not have to become
/// one. It hands over an address and one master secret and gets back something to send with, count
/// with, and stop.
/// </para>
/// <para>
/// THE TOKENS ARE MINTED HERE AND HANDED OUT FROM HERE, because only the coordinator ever holds the
/// secret. Traffic in a swarm is one-way - nothing calls in - so a Hive cannot ask for a token, and is
/// given both its own and the one to pass to its Workers before it starts. This sample prints a line of
/// JSON that the sample Hive reads on its standard input, which is a way of handing them over that
/// leaves them nowhere a command line or an environment variable can be read from.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The kind of message the Hives and Workers of this sample listen for. A coordinator may define
    /// as many kinds as it likes; the only rule is that a kind must not begin with the prefix the
    /// swarm keeps for its own.
    /// </summary>
    private const string NoteMessageKind = "sample.note";

    private const string UrlSwitch = "--url=";

    private static async Task<int> Main(string[] args)
    {
        using var stopping = new CancellationTokenSource();

        Console.CancelKeyPress += (_, keyPress) =>
        {
            keyPress.Cancel = true;
            stopping.Cancel();
        };

        var options = new SwarmQueenOptions
        {
            Url = ReadSwitch(args, UrlSwitch) ?? SwarmQueenOptions.AnyFreeLoopbackPortUrl,

            //A sample invents one every time it runs. A real application keeps one and hands the same
            //one to every coordinator it starts, because every token in the swarm is derived from it.
            MasterSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };

        await using var queen = await SwarmQueen.StartAsync(options, stopping.Token).ConfigureAwait(false);

        queen.Connected += (_, connection) => Console.WriteLine(
            "connected: " + connection.Role + " (" + Counts(connection.HiveCount, connection.WorkerCount) + ")");

        queen.Disconnected += (_, connection) => Console.WriteLine(
            "disconnected: " + connection.Role + " (" + Counts(connection.HiveCount, connection.WorkerCount) + ")");

        Console.WriteLine("listening on " + queen.BaseUrl);
        Console.WriteLine("  hive hub:   " + queen.HiveHubUrl);
        Console.WriteLine("  worker hub: " + queen.WorkerHubUrl);
        Console.WriteLine();
        Console.WriteLine("Settings for one host, to hand to a Hive on its standard input. Fill in the");
        Console.WriteLine("path of the Worker program before using it:");
        Console.WriteLine();
        Console.WriteLine(HiveSettingsLine(queen));
        Console.WriteLine();
        Console.WriteLine("Commands, one per line: note-hives, note-workers, terminate, counts, quit.");

        await ReadCommandsAsync(queen, stopping).ConfigureAwait(false);

        await queen.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine("stopped");
        return 0;
    }

    private static async Task ReadCommandsAsync(SwarmQueen queen, CancellationTokenSource stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            string line;

            try
            {
                line = await Console.In.ReadLineAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (line == null)
            {
                //Nothing is typing at this program. Wait to be stopped instead of reading an ended
                //stream over and over.
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            switch (line?.Trim().ToLowerInvariant())
            {
                case "note-hives":
                    await queen.SendToHivesAsync(
                            SwarmMessage.Create(NoteMessageKind, new { note = "a note for every Hive" }),
                            stopping.Token)
                        .ConfigureAwait(false);
                    break;

                case "note-workers":
                    await queen.SendToWorkersAsync(
                            SwarmMessage.Create(NoteMessageKind, new { note = "a note for every Worker" }),
                            stopping.Token)
                        .ConfigureAwait(false);
                    break;

                case "terminate":
                    //The work is over. Every Hive ends its Workers and finishes, and nothing resumes.
                    await queen.SendTerminateAllWorkersToHivesAsync(stopping.Token).ConfigureAwait(false);
                    break;

                case "counts":
                    Console.WriteLine(Counts(queen.HiveCount, queen.WorkerCount));
                    break;

                case "quit":
                    return;

                default:
                    Console.WriteLine(
                        "Commands: note-hives, note-workers, terminate, counts, quit.");
                    break;
            }
        }
    }

    private static string HiveSettingsLine(SwarmQueen queen)
    {
        var settings = new
        {
            queenUrl = queen.BaseUrl,
            hiveToken = queen.CreateHiveToken(),
            workerToken = queen.CreateWorkerToken(),
            workerExecutable = "/path/to/SwarmSample.Worker",
            maxWorkers = 5,
            workerSteps = 0,
            workerStepMilliseconds = 250,
            report = true
        };

        return JsonSerializer.Serialize(settings);
    }

    private static string Counts(int hives, int workers)
        => hives.ToString(CultureInfo.InvariantCulture) + " hive(s), "
           + workers.ToString(CultureInfo.InvariantCulture) + " worker(s)";

    private static string ReadSwitch(string[] args, string prefix)
    {
        if (args == null)
        {
            return null;
        }

        foreach (var argument in args)
        {
            if (argument != null && argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = argument[prefix.Length..].Trim();

                if (value.Length > 0)
                {
                    return value;
                }
            }
        }

        return null;
    }
}
