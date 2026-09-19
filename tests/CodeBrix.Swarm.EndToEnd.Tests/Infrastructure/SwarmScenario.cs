using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Diagnostics;
using CodeBrix.Swarm.Hive;
using CodeBrix.Swarm.Queen;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// One scenario's worth of a real swarm: a real coordinator on a free port of the loopback interface, a
/// real Hive running in this process, and real Worker processes started from the sample program.
/// </summary>
/// <remarks>
/// <para>
/// EVERY INTERVAL IS SHORTENED through the options, which is the whole reason they are options. A Hive
/// that settles for five seconds between Workers and waits a minute for a coordinator is right for a
/// swarm and hopeless for a suite; the same Hive with the same code settles for a tenth of a second here.
/// </para>
/// <para>
/// The ONE thing substituted is the host reading. See <see cref="QuietHostProbe" /> for why.
/// </para>
/// </remarks>
internal sealed class SwarmScenario : IAsyncDisposable
{
    /// <summary>
    /// How long any wait in this suite will put up with before it gives up and says what it was waiting
    /// for. Generously long: it is a limit on a suite that has gone wrong, not a timing assertion.
    /// </summary>
    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The one secret every access token in a scenario's swarm is derived from. A real application keeps
    /// one of its own and never lets it out of the coordinator; a suite needs it in order to mint a token
    /// the coordinator's own surface will not mint, such as one that expired five minutes ago.
    /// </summary>
    public const string MasterSecret = "a-master-secret-long-enough-for-a-swarm-of-workers";

    private readonly object _gate = new();
    private readonly List<WorkerExit> _exits = [];
    private readonly List<WorkerOutputLine> _output = [];
    private readonly List<string> _report = [];
    private readonly CancellationTokenSource _endingTheHive = new();

    private SwarmQueen _queen;
    private Task<SwarmHiveOutcome> _hiveRun;
    private int _timesAsked;
    private bool _isDisposed;

    private SwarmScenario(SwarmQueen queen, string queenUrl)
    {
        _queen = queen;
        QueenUrl = queenUrl;
    }

    /// <summary>
    /// Starts a real coordinator on a free port and hands back a scenario built round it.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to give up on starting.</param>
    /// <returns>The scenario.</returns>
    public static async Task<SwarmScenario> StartAsync(CancellationToken cancellationToken)
    {
        var queen = await SwarmQueen.StartAsync(
                new SwarmQueenOptions
                {
                    Url = SwarmQueenOptions.AnyFreeLoopbackPortUrl,
                    MasterSecret = MasterSecret
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new SwarmScenario(queen, queen.BaseUrl);
    }

    /// <summary>
    /// A scenario whose coordinator address is a port nothing is listening on, for the checks about what
    /// a Hive or a Worker does when it cannot reach one.
    /// </summary>
    /// <returns>The scenario. Its <see cref="Queen" /> is null.</returns>
    public static SwarmScenario WithNoCoordinator()
        => new(null, "http://127.0.0.1:" + FindAFreePort().ToString(CultureInfo.InvariantCulture));

    /// <summary>The running coordinator, or null when this scenario has none.</summary>
    public SwarmQueen Queen => _queen;

    /// <summary>The coordinator's base address, whether or not one is listening on it.</summary>
    public string QueenUrl { get; }

    /// <summary>Starts real Worker processes and keeps a record of every one.</summary>
    public RecordingWorkerProcessFactory Processes { get; } = new();

    /// <summary>The substituted host reading.</summary>
    public QuietHostProbe Host { get; } = new();

    /// <summary>
    /// What the Hive is told to start next. A scenario replaces this; the default is one endless Worker
    /// after another.
    /// </summary>
    public Func<SwarmHiveContext, CancellationToken, Task<WorkerLaunch>> NextWorker { get; set; }

    /// <summary>How many times the Hive has asked what to start next.</summary>
    public int TimesAsked => Volatile.Read(ref _timesAsked);

    /// <summary>Every Worker ending the Hive has told this scenario about.</summary>
    public IReadOnlyList<WorkerExit> Exits
    {
        get
        {
            lock (_gate)
            {
                return _exits.ToArray();
            }
        }
    }

    /// <summary>Every line collected from a Worker, when the Hive was asked to collect them.</summary>
    public IReadOnlyList<WorkerOutputLine> Output
    {
        get
        {
            lock (_gate)
            {
                return _output.ToArray();
            }
        }
    }

    /// <summary>Everything the Hive reported about its own doings.</summary>
    public IReadOnlyList<string> Report
    {
        get
        {
            lock (_gate)
            {
                return _report.ToArray();
            }
        }
    }

    /// <summary>
    /// The options a scenario's Hive runs with: the real ones, with every interval shortened and a limit
    /// of five Workers.
    /// </summary>
    /// <param name="hiveId">What to call this Hive. Give each scenario its own.</param>
    /// <returns>The options, for the scenario to adjust before starting the Hive.</returns>
    public SwarmHiveOptions HiveOptions(string hiveId)
    {
        var options = new SwarmHiveOptions
        {
            QueenUrl = QueenUrl,
            HiveToken = _queen == null ? "no-coordinator-to-mint-one" : _queen.CreateHiveToken(),
            WorkerToken = _queen == null ? "no-coordinator-to-mint-one" : _queen.CreateWorkerToken(),
            HiveId = hiveId,

            //Jeremy asked for the Hive to be held to five Workers in these scenarios.
            Limits = { MaxWorkers = 5 },

            QueenUnreachableWindow = TimeSpan.FromSeconds(3),
            FirstRetryDelay = TimeSpan.FromMilliseconds(100),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(250),
            PollInterval = TimeSpan.FromMilliseconds(50),
            SettleInterval = TimeSpan.FromMilliseconds(100),
            NotNowInterval = TimeSpan.FromMilliseconds(100),
            ImmediateFailureWindow = TimeSpan.FromSeconds(5),
            FirstBackoff = TimeSpan.FromSeconds(5),
            MaximumBackoff = TimeSpan.FromSeconds(20),
            BackoffResetAfter = TimeSpan.FromSeconds(30),
            TerminationGracePeriod = TimeSpan.FromSeconds(5),

            WorkerExited = exit =>
            {
                lock (_gate)
                {
                    _exits.Add(exit);
                }
            },

            WorkerOutput = line =>
            {
                lock (_gate)
                {
                    _output.Add(line);
                }
            },

            Report = line =>
            {
                lock (_gate)
                {
                    _report.Add(line);
                }
            }
        };

        options.NextWorkerAsync = (context, cancellationToken) =>
        {
            Interlocked.Increment(ref _timesAsked);

            var next = NextWorker;

            return next == null
                ? Task.FromResult(WorkerLaunch.StartWithWork(
                    SampleWorkerProgram.Path, [], SampleWorkerProgram.RunsUntilEnded()))
                : next(context, cancellationToken);
        };

        return options;
    }

    /// <summary>
    /// Starts the Hive. It runs in this process, with the real connection to the real coordinator and
    /// real Worker processes, until something ends it.
    /// </summary>
    /// <param name="options">The options, from <see cref="HiveOptions" />.</param>
    public void StartHive(SwarmHiveOptions options)
    {
        _hiveRun = SwarmHiveHost.RunAsync(
            options,
            Host,
            Processes,
            SignalRHubConnectionFactory.Instance,
            SystemSwarmClock.Instance,
            _endingTheHive.Token);
    }

    /// <summary>Ends the Hive the way a consuming application would.</summary>
    public void EndTheHive()
    {
        try
        {
            _endingTheHive.Cancel();
        }
        catch (ObjectDisposedException)
        {
            //The scenario is already over.
        }
    }

    /// <summary>
    /// Waits for the Hive to finish and says why it did.
    /// </summary>
    /// <returns>The Hive's outcome.</returns>
    public async Task<SwarmHiveOutcome> WaitForHiveAsync()
    {
        if (_hiveRun == null)
        {
            throw new InvalidOperationException("This scenario has not started a Hive.");
        }

        return await _hiveRun.WaitAsync(WaitLimit).ConfigureAwait(false);
    }

    /// <summary>True once the Hive has finished.</summary>
    public bool HiveHasFinished => _hiveRun != null && _hiveRun.IsCompleted;

    /// <summary>
    /// Waits until something is true, and says what it was waiting for if it never becomes true.
    /// </summary>
    /// <param name="condition">What is being waited for.</param>
    /// <param name="what">How to describe it if the wait runs out.</param>
    /// <param name="cancellationToken">Cancelled to stop waiting.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        string what,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + WaitLimit;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Waited " + WaitLimit.TotalSeconds.ToString(CultureInfo.InvariantCulture)
            + " seconds for: " + what);
    }

    /// <summary>
    /// Waits a little for something to be true, and says whether it became true. For checking that
    /// something does NOT happen, where waiting the full limit would only slow the suite down.
    /// </summary>
    /// <param name="condition">What is being looked for.</param>
    /// <param name="within">How long to look for it.</param>
    /// <param name="cancellationToken">Cancelled to stop waiting.</param>
    /// <returns>True when it became true within the time.</returns>
    public static async Task<bool> BecomesTrueAsync(
        Func<bool> condition,
        TimeSpan within,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + within;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
        }

        return condition();
    }

    /// <summary>
    /// The check EVERY scenario in this suite ends with: no Worker process this suite caused to exist is
    /// still running, anywhere on this machine.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to stop waiting.</param>
    /// <returns>A task that completes once the check has been made.</returns>
    public static async Task AssertNoWorkerProcessesAreLeftAsync(CancellationToken cancellationToken)
    {
        var left = await SpawnedProcesses
            .WaitUntilNoneAreLeftAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        if (left.Count == 0)
        {
            return;
        }

        var stopped = SpawnedProcesses.StopAnythingLeft();

        throw new InvalidOperationException(
            left.Count.ToString(CultureInfo.InvariantCulture)
            + " Worker process(es) were still running after the scenario finished: "
            + string.Join(", ", left) + ". "
            + stopped.ToString(CultureInfo.InvariantCulture)
            + " of them had to be stopped so that the rest of this suite could run.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        EndTheHive();

        var run = _hiveRun;
        _hiveRun = null;

        if (run != null)
        {
            try
            {
                await run.WaitAsync(WaitLimit).ConfigureAwait(false);
            }
            catch (Exception)
            {
                //A scenario that went wrong must still put the machine back as it found it, which the
                //sweep below does.
            }
        }

        var queen = _queen;
        _queen = null;

        if (queen != null)
        {
            try
            {
                await queen.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                //The coordinator is being thrown away regardless.
            }
        }

        _endingTheHive.Dispose();

        //The last word: nothing this scenario started is left on the machine, whatever happened.
        SpawnedProcesses.StopAnythingLeft();
    }

    private static int FindAFreePort()
    {
        //Taken and given back, so that the address is one nothing is listening on. A port that has just
        //been released is the nearest thing to a certainly-closed one that can be asked for, and even if
        //something else took it in between, it would not be speaking the coordinator's protocol.
        var listener = new TcpListener(IPAddress.Loopback, 0);

        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
            listener.Dispose();
        }
    }
}
