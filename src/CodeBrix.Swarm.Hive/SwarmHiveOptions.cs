using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Connections;

namespace CodeBrix.Swarm.Hive;

/// <summary>
/// What the consuming application hands a Hive: where the coordinator is, the two access tokens, the
/// step that answers "what do I start next?", how much of the host to leave alone, and how patient to
/// be about everything.
/// </summary>
/// <remarks>
/// <para>
/// THERE ARE TWO TOKENS because traffic in a swarm is one-way and a Hive can therefore never ask the
/// coordinator for anything. <see cref="HiveToken" /> is this Hive's own, and
/// <see cref="WorkerToken" /> is the one it passes on, unchanged, in every Worker's configuration.
/// The application that owns the coordinator mints both and hands them to the host this Hive runs on.
/// </para>
/// <para>
/// Every interval here is an option with a sensible default. Shortening them all is what makes a
/// swarm testable; lengthening them is what makes one gentle on a host that is shared with something
/// else.
/// </para>
/// </remarks>
public sealed class SwarmHiveOptions
{
    /// <summary>
    /// The coordinator's base address, such as <c>http://192.168.1.10:5000</c>. The Hive appends the
    /// Hive hub's path itself, and passes the same base address on to every Worker.
    /// </summary>
    public string QueenUrl { get; set; }

    /// <summary>
    /// This Hive's own access token, which is only good at the coordinator's Hive hub. The
    /// coordinator minted it; this Hive cannot read it and does not need to.
    /// </summary>
    public string HiveToken { get; set; }

    /// <summary>
    /// The access token every Worker this Hive starts is given, which is only good at the
    /// coordinator's Worker hub. The Hive passes it on unchanged and never looks inside it.
    /// </summary>
    public string WorkerToken { get; set; }

    /// <summary>
    /// What this Hive is called, or null for the host's own name. Every Worker is named after it, so
    /// two Hives on one host should be given names of their own.
    /// </summary>
    public string HiveId { get; set; }

    /// <summary>
    /// Asked whenever the Hive has room for another Worker. Answer with
    /// <see cref="WorkerLaunch.Start(string)" /> and its companions, or with
    /// <see cref="WorkerLaunch.NotNow" /> when there is nothing to hand out yet - "not now" is never
    /// a failure, and the Hive simply asks again after <see cref="NotNowInterval" />.
    /// </summary>
    public Func<SwarmHiveContext, CancellationToken, Task<WorkerLaunch>> NextWorkerAsync { get; set; }

    /// <summary>
    /// Run before the Hive connects to the coordinator. This is where to register handlers for
    /// application-defined messages, so that nothing sent in the first moments is missed. Optional.
    /// </summary>
    public Func<SwarmHiveContext, CancellationToken, Task> ConfigureAsync { get; set; }

    /// <summary>
    /// Run once the Hive has finished, after every Worker has been ended and before the run returns.
    /// The context's outcome says why. It is given an uncancelled token, because this is the last
    /// chance to put things down tidily. Optional.
    /// </summary>
    public Func<SwarmHiveContext, CancellationToken, Task> ShutdownAsync { get; set; }

    /// <summary>
    /// How much of the host this Hive may take up. The defaults are also ceilings; see
    /// <see cref="SwarmHostLimits" />.
    /// </summary>
    public SwarmHostLimits Limits { get; set; } = new();

    /// <summary>
    /// How long the Hive keeps trying to reach the coordinator before giving up - at start-up and
    /// again after losing the connection. Throughout the window it keeps the Workers it already has
    /// running and starts nothing new. When the window runs out it ends them in an orderly way and
    /// finishes.
    /// </summary>
    public TimeSpan QueenUnreachableWindow { get; set; } = SwarmConnectionOptions.DefaultQueenUnreachableWindow;

    /// <summary>
    /// How long to wait after the first attempt to reach the coordinator fails. Each further wait is
    /// twice the last, up to <see cref="MaximumRetryDelay" />.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The longest wait between attempts to reach the coordinator.</summary>
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long to wait after starting a Worker, and after deciding the host has no room, before
    /// measuring the host again. Workers are started ONE AT A TIME with this wait between them: a
    /// Worker that has only just started has not yet taken up the memory it is going to, so starting
    /// a second one on the strength of a reading taken before the first had settled is how a host
    /// ends up oversubscribed.
    /// </summary>
    public TimeSpan SettleInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait before asking the application again, after it answered
    /// <see cref="WorkerLaunch.NotNow" />.
    /// </summary>
    public TimeSpan NotNowInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the Hive looks round while it is not doing anything else - noticing that a Worker
    /// has ended, that the connection has come back, or that a Worker it asked to end has gone.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How soon after starting a Worker must fail for the failure to count as an immediate one and
    /// set the wait before the next start. A Worker that runs for longer than this and then fails has
    /// at least done some work.
    /// </summary>
    public TimeSpan ImmediateFailureWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait before starting another Worker after the first immediate failure. Each
    /// further immediate failure doubles it, up to <see cref="MaximumBackoff" />.
    /// </summary>
    public TimeSpan FirstBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest the Hive will ever wait because of immediate failures.</summary>
    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a Worker has to survive for the Hive to forget about the failures before it and
    /// start the next one straight away.
    /// </summary>
    public TimeSpan BackoffResetAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the Hive waits for its Workers to end by themselves after it closes their lifelines,
    /// before it stops whatever is left the hard way - each remaining Worker and everything it
    /// started.
    /// </summary>
    public TimeSpan TerminationGracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// True to collect what the Workers write and hand each line to <see cref="WorkerOutput" />.
    /// It is off by default: a Worker's output then goes wherever the Hive's own goes, which costs
    /// nothing and cannot go wrong.
    /// </summary>
    public bool CaptureWorkerOutput { get; set; }

    /// <summary>
    /// Where each collected line goes, when <see cref="CaptureWorkerOutput" /> is on. Lines are read
    /// from the Workers continuously whether or not this is set.
    /// </summary>
    public Action<WorkerOutputLine> WorkerOutput { get; set; }

    /// <summary>
    /// Told about every Worker that ends, with the code it ended with and how long it ran. Optional.
    /// </summary>
    public Action<WorkerExit> WorkerExited { get; set; }

    /// <summary>
    /// Somewhere to put a line about anything the Hive dealt with by itself - a failed connection
    /// attempt, a Worker that would not start, a reading of the host it could not take. Optional;
    /// nothing is written anywhere when it is not set.
    /// </summary>
    public Action<string> Report { get; set; }

    /// <summary>
    /// Checks that everything needed is present and makes sense together.
    /// </summary>
    /// <exception cref="SwarmConfigurationException">Something is missing or out of range.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueenUrl))
        {
            throw new SwarmConfigurationException(
                "The coordinator's base address is missing.");
        }

        if (!Uri.TryCreate(QueenUrl.Trim(), UriKind.Absolute, out var queen)
            || (queen.Scheme != Uri.UriSchemeHttp && queen.Scheme != Uri.UriSchemeHttps))
        {
            throw new SwarmConfigurationException(
                $"The coordinator's base address must be an absolute http or https address, and was "
                + $"'{QueenUrl}'. Every Worker this Hive starts is given the same address, so a Hive "
                + "that accepted one it could not use would hand it on to every Worker as well.");
        }

        if (string.IsNullOrWhiteSpace(HiveToken))
        {
            throw new SwarmConfigurationException(
                "This Hive's own access token is missing. Only the coordinator can mint one, and it "
                + "reaches a Hive from outside the swarm.");
        }

        if (string.IsNullOrWhiteSpace(WorkerToken))
        {
            throw new SwarmConfigurationException(
                "The access token to pass on to the Workers is missing. A Hive cannot ask the "
                + "coordinator for one - traffic in a swarm is one-way - so it is handed both tokens "
                + "when it starts.");
        }

        if (NextWorkerAsync == null)
        {
            throw new SwarmConfigurationException(
                "A Hive has nothing to start: set NextWorkerAsync to the step that answers what to "
                + "start next, or WorkerLaunch.NotNow when there is nothing yet.");
        }

        if (Limits == null)
        {
            throw new SwarmConfigurationException(
                "The host limits are missing. Leave them at their defaults rather than removing them.");
        }

        Limits.Validate();

        if (QueenUnreachableWindow < TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The window for reaching the coordinator must not be negative.");
        }

        if (FirstRetryDelay <= TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The first retry delay must be longer than nothing.");
        }

        if (MaximumRetryDelay < FirstRetryDelay)
        {
            throw new SwarmConfigurationException(
                "The longest retry delay must not be shorter than the first one.");
        }

        RequirePositive(SettleInterval, "The settle interval between starting Workers");
        RequirePositive(NotNowInterval, "The wait before asking what to start next again");
        RequirePositive(PollInterval, "The interval the Hive looks round on");
        RequirePositive(ImmediateFailureWindow, "The window an immediate failure counts within");
        RequirePositive(FirstBackoff, "The first wait after an immediate failure");
        RequirePositive(BackoffResetAfter, "How long a Worker must survive to clear the waits");

        if (MaximumBackoff < FirstBackoff)
        {
            throw new SwarmConfigurationException(
                "The longest wait after immediate failures must not be shorter than the first one.");
        }

        if (TerminationGracePeriod < TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The grace period for Workers to end by themselves must not be negative.");
        }
    }

    private static void RequirePositive(TimeSpan value, string what)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new SwarmConfigurationException($"{what} must be longer than nothing.");
        }
    }
}
