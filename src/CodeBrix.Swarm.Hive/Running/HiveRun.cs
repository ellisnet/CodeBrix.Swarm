using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Diagnostics;
using CodeBrix.Swarm.Hive.HostLoad;
using CodeBrix.Swarm.Hive.Spawning;

namespace CodeBrix.Swarm.Hive.Running;

/// <summary>
/// One run of a Hive, from the moment it subscribes to the coordinator to the moment its last Worker
/// has gone.
/// </summary>
/// <remarks>
/// <para>
/// THE ORDER OF THINGS. Subscribe first, and start nothing until that has worked at least once - a
/// Hive that started Workers before it knew where its coordinator was would have no way of ending
/// them. Then, over and over: notice which Workers have ended; decide whether the host has room for
/// another; ask the application what to start; start ONE; wait for it to settle; measure again. When
/// something ends the run - the coordinator saying the work is over, the coordinator going out of
/// reach for too long, or the application's own token - close every Worker's lifeline, give them a
/// grace period to go by themselves, stop whatever is left, and report why.
/// </para>
/// <para>
/// WHILE THE COORDINATOR IS OUT OF REACH the Workers already running are left entirely alone and
/// nothing new is started. The connection retries by its own rule; only when that rule runs out does
/// the Hive wind down. A brief interruption therefore costs a swarm nothing at all.
/// </para>
/// <para>
/// Every wait goes through the clock, so a run whose intervals are measured in minutes finishes in
/// microseconds under a clock that does not sleep.
/// </para>
/// </remarks>
internal sealed class HiveRun
{
    //How many polling passes a stopped Worker is given to actually go before the Hive lets go of it.
    private const int ForcedStopPollPasses = 20;

    private readonly SwarmHiveOptions _options;
    private readonly SwarmHostLimits _limits;
    private readonly string _hiveId;
    private readonly IHostLoadProbe _probe;
    private readonly IWorkerProcessFactory _processes;
    private readonly ISwarmClock _clock;
    private readonly WorkerRoster _roster;
    private readonly CpuAverageWindow _cpuAverage;
    private readonly CrashBackoff _backoff;

    private SpawnRefusal? _lastReportedRefusal;

    public HiveRun(
        SwarmHiveOptions options,
        SwarmHostLimits limits,
        string hiveId,
        IHostLoadProbe probe,
        IWorkerProcessFactory processes,
        ISwarmClock clock)
    {
        _options = options;
        _limits = limits;
        _hiveId = hiveId;
        _probe = probe;
        _processes = processes;
        _clock = clock;
        _roster = new WorkerRoster(hiveId);
        _cpuAverage = new CpuAverageWindow(limits.CpuAveragingWindow);
        _backoff = new CrashBackoff(
            options.FirstBackoff,
            options.MaximumBackoff,
            options.ImmediateFailureWindow,
            options.BackoffResetAfter);
    }

    /// <summary>
    /// Runs the Hive until something ends it.
    /// </summary>
    /// <param name="connections">What makes the connection to the coordinator.</param>
    /// <param name="cancellationToken">Cancelled to end the run from outside.</param>
    /// <returns>Why the Hive finished.</returns>
    public async Task<SwarmHiveOutcome> RunAsync(
        ISwarmHubConnectionFactory connections,
        CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ending = new HiveEnding(stopping);

        using var fromOutside = cancellationToken.Register(
            () => ending.Record(SwarmHiveOutcome.Cancelled));

        var connectionOptions = new SwarmConnectionOptions
        {
            QueenUrl = _options.QueenUrl,
            Token = _options.HiveToken,
            QueenUnreachableWindow = _options.QueenUnreachableWindow,
            FirstRetryDelay = _options.FirstRetryDelay,
            MaximumRetryDelay = _options.MaximumRetryDelay
        };

        await using var client = new SwarmHubClient(
            SwarmRole.Hive, connectionOptions, connections, _clock);

        var context = new SwarmHiveContext(
            _hiveId,
            client.Messages,
            () => _roster.Count,
            () => _roster.StartedCount,
            () => client.IsConnected);

        //The one built-in message a Hive answers. It means the work is over: end the Workers, finish,
        //and never start another. There is no message that undoes it.
        client.OnTerminate(_ =>
        {
            Report("The coordinator says the work is over.");
            ending.Request(SwarmHiveOutcome.ToldToTerminate);
            return Task.CompletedTask;
        });

        client.QueenUnreachable += (_, _) =>
        {
            Report("The coordinator has been out of reach for the whole retry window.");
            ending.Request(SwarmHiveOutcome.QueenUnreachable);
        };

        client.Error += (_, error) => Report(Describe(error));

        try
        {
            await ConfigureAsync(context, ending, stopping.Token).ConfigureAwait(false);

            if (!ending.IsRequested)
            {
                //CANNOT START ANYTHING UNTIL THE COORDINATOR HAS BEEN REACHED ONCE.
                var connected = await client.StartAsync(stopping.Token).ConfigureAwait(false);

                if (!connected)
                {
                    ending.Record(SwarmHiveOutcome.QueenUnreachable);
                }
            }

            if (!ending.IsRequested)
            {
                await RunLoopAsync(context, client, ending, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            //Something ended the run; the reason is already recorded, or it came from outside.
            ending.Record(SwarmHiveOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            ending.Record(SwarmHiveOutcome.Failed);
            Report("The Hive stopped because of an unexpected failure: " + ex.Message);
        }

        await EndEveryWorkerAsync().ConfigureAwait(false);

        context.Outcome = ending.Outcome;

        await ShutdownAsync(context).ConfigureAwait(false);

        try
        {
            await client.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report("The Hive's connection did not close tidily: " + ex.Message);
        }

        _roster.Clear();

        return ending.Outcome;
    }

    private async Task ConfigureAsync(
        SwarmHiveContext context,
        HiveEnding ending,
        CancellationToken cancellationToken)
    {
        if (_options.ConfigureAsync == null)
        {
            return;
        }

        try
        {
            await _options.ConfigureAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            //Something ended the run before the Hive had finished setting itself up.
        }
        catch (Exception ex)
        {
            ending.Record(SwarmHiveOutcome.Failed);
            Report("The Hive's configure step threw: " + ex.Message);
        }
    }

    private async Task ShutdownAsync(SwarmHiveContext context)
    {
        if (_options.ShutdownAsync == null)
        {
            return;
        }

        try
        {
            //An uncancelled token: this is the last chance to put things down tidily.
            await _options.ShutdownAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report("The Hive's shutdown step threw: " + ex.Message);
        }
    }

    private async Task RunLoopAsync(
        SwarmHiveContext context,
        SwarmHubClient client,
        HiveEnding ending,
        CancellationToken cancellationToken)
    {
        while (!ending.IsRequested && !cancellationToken.IsCancellationRequested)
        {
            NoticeEndedWorkers(feedTheBackoff: true);

            if (ending.IsRequested || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var wait = await DecideAsync(context, client, ending, cancellationToken)
                .ConfigureAwait(false);

            if (ending.IsRequested || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (wait > TimeSpan.Zero)
            {
                await _clock.DelayAsync(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One pass: decide whether to start a Worker, start at most one, and say how long to wait before
    /// the next pass.
    /// </summary>
    private async Task<TimeSpan> DecideAsync(
        SwarmHiveContext context,
        SwarmHubClient client,
        HiveEnding ending,
        CancellationToken cancellationToken)
    {
        //THE COORDINATOR IS OUT OF REACH. The connection is retrying by its own rule, and it will say
        //so when that rule runs out. Until then the Workers already running carry on and nothing new
        //is started: the interruption may last a moment, and ending good work over a moment would be
        //worse than waiting.
        if (!client.IsConnected)
        {
            return _options.PollInterval;
        }

        var remainingBackoff = _backoff.RemainingAt(_clock.UtcNow);

        if (remainingBackoff > TimeSpan.Zero)
        {
            //Waited through in short steps rather than one long one, so that a Worker ending part way
            //through is still noticed when it happens.
            return remainingBackoff < _options.PollInterval ? remainingBackoff : _options.PollInterval;
        }

        var reading = await _probe.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (reading != null && reading.IsUsable)
        {
            _cpuAverage.Add(_clock.UtcNow, reading.CpuBusyPercent);
        }

        var refusal = SpawnAdmission.Evaluate(
            reading,
            _cpuAverage.Average(_clock.UtcNow),
            _roster.Count,
            _roster.AverageWorkingSetBytes(),
            _limits);

        if (refusal != SpawnRefusal.None)
        {
            ReportRefusal(refusal, reading);
            return _options.SettleInterval;
        }

        _lastReportedRefusal = null;

        var launch = await AskWhatToStartAsync(context, ending, cancellationToken).ConfigureAwait(false);

        if (ending.IsRequested)
        {
            return TimeSpan.Zero;
        }

        //"Not now" is not a failure and never counts against anything. The application is simply not
        //ready, and it is asked again in a moment.
        if (launch == null || launch.IsNotNow)
        {
            return _options.NotNowInterval;
        }

        StartOneWorker(launch);

        //ONE AT A TIME. The Worker that has just started has not yet taken up the memory it is going
        //to, so the next decision is made only after it has had a chance to.
        return _options.SettleInterval;
    }

    private async Task<WorkerLaunch> AskWhatToStartAsync(
        SwarmHiveContext context,
        HiveEnding ending,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _options.NextWorkerAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            ending.Request(SwarmHiveOutcome.Failed);
            Report("The step that says what to start next threw: " + ex.Message);
            return null;
        }
    }

    private void StartOneWorker(WorkerLaunch launch)
    {
        var workerId = _roster.NextWorkerId();

        var configuration = new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings
            {
                QueenUrl = _options.QueenUrl,
                //Passed on exactly as it arrived. A Hive cannot read a token and has no reason to.
                Token = _options.WorkerToken,
                WorkerId = workerId
            },
            Work = launch.Work
        };

        var request = new WorkerProcessRequest(
            workerId,
            launch.Executable,
            launch.Arguments,
            launch.WorkingDirectory,
            configuration.ToJsonLine(),
            _options.CaptureWorkerOutput,
            _options.WorkerOutput);

        try
        {
            var worker = _processes.Start(request);
            _roster.Add(worker);

            Report(
                "Started Worker " + workerId + " as process "
                + worker.ProcessId.ToString(CultureInfo.InvariantCulture) + ".");
        }
        catch (Exception ex)
        {
            //A Worker that cannot be started at all will not start next time either, so the wait goes
            //up exactly as it would after one that failed the instant it ran.
            _backoff.RecordFailedStart(_clock.UtcNow);

            Report(
                "Worker " + workerId + " could not be started from '" + launch.Executable + "': "
                + ex.Message);
        }
    }

    private void NoticeEndedWorkers(bool feedTheBackoff)
    {
        var exits = _roster.Reap(_clock.UtcNow, _options.ImmediateFailureWindow);

        foreach (var exit in exits)
        {
            if (feedTheBackoff)
            {
                _backoff.RecordExit(_clock.UtcNow, exit.Ran, exit.ExitCode);
            }

            Report(DescribeExit(exit));

            var told = _options.WorkerExited;

            if (told == null)
            {
                continue;
            }

            try
            {
                told(exit);
            }
            catch (Exception)
            {
                //Telling the application is best effort; something that throws while being told about
                //an ending must not end the Hive.
            }
        }
    }

    /// <summary>
    /// Ends every Worker: close the lifelines, wait for them to go by themselves, stop what is left.
    /// </summary>
    private async Task EndEveryWorkerAsync()
    {
        if (_roster.Count == 0)
        {
            NoticeEndedWorkers(feedTheBackoff: false);
            return;
        }

        Report(
            "Ending " + _roster.Count.ToString(CultureInfo.InvariantCulture)
            + " Worker(s): closing their lifelines.");

        //POLITELY FIRST. The end of the lifeline is how a Worker is asked to wind itself down, and a
        //Worker that is in the middle of something gets the chance to finish it.
        _roster.CloseAllLifelines();

        await WaitForWorkersToEndAsync(_options.TerminationGracePeriod).ConfigureAwait(false);

        //AND THEN NOT POLITELY. Whatever is left has had its grace period, and a host must not be
        //left with Workers on it - nor with anything they started.
        var stopped = _roster.ForceStopAll();

        if (stopped > 0)
        {
            Report(
                stopped.ToString(CultureInfo.InvariantCulture)
                + " Worker(s) did not end within the grace period and were stopped, along with "
                + "everything they had started.");

            //A process that has been stopped takes a moment to actually go.
            await WaitForWorkersToEndAsync(
                    TimeSpan.FromTicks(_options.PollInterval.Ticks * ForcedStopPollPasses))
                .ConfigureAwait(false);
        }

        NoticeEndedWorkers(feedTheBackoff: false);
    }

    private async Task WaitForWorkersToEndAsync(TimeSpan within)
    {
        if (within <= TimeSpan.Zero)
        {
            return;
        }

        var deadline = _clock.UtcNow + within;

        while (_clock.UtcNow < deadline && !_roster.AllHaveExited())
        {
            await _clock.DelayAsync(_options.PollInterval, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void ReportRefusal(SpawnRefusal refusal, HostLoadReading reading)
    {
        //Said once per change of reason. A Hive on a full host would otherwise repeat itself every few
        //seconds for as long as it ran.
        if (_lastReportedRefusal == refusal)
        {
            return;
        }

        _lastReportedRefusal = refusal;

        Report("Not starting another Worker: " + DescribeRefusal(refusal, reading));
    }

    private string DescribeRefusal(SpawnRefusal refusal, HostLoadReading reading)
    {
        switch (refusal)
        {
            case SpawnRefusal.AtWorkerLimit:
                return "this Hive already has the "
                       + (_limits.MaxWorkers.HasValue
                           ? _limits.MaxWorkers.Value.ToString(CultureInfo.InvariantCulture)
                           : "maximum")
                       + " Workers it is allowed.";

            case SpawnRefusal.RamPercentReached:
                return "another one would take the host's memory past "
                       + _limits.MaxRamPercent.ToString(CultureInfo.InvariantCulture)
                       + " percent in use (it is at "
                       + Round(reading == null ? 0d : reading.UsedRamPercent)
                       + " percent now).";

            case SpawnRefusal.FreeRamFloorReached:
                return "another one would leave the host with less than "
                       + Mebibytes(_limits.FreeRamFloorBytes)
                       + " MiB free (it has "
                       + Mebibytes(reading == null ? 0L : reading.AvailableRamBytes)
                       + " MiB now).";

            case SpawnRefusal.CpuPercentReached:
                return "the host's processor has been busier than "
                       + _limits.MaxCpuPercent.ToString(CultureInfo.InvariantCulture)
                       + " percent over the averaging window (it is averaging "
                       + Round(_cpuAverage.Average(_clock.UtcNow))
                       + " percent).";

            case SpawnRefusal.HostLoadUnknown:
                return "the host could not be measured, so there is no telling whether it has room.";

            default:
                return "there is no room.";
        }
    }

    private static string DescribeExit(WorkerExit exit)
    {
        var how = exit.IsFailure
            ? "failed with code " + exit.ExitCode.ToString(CultureInfo.InvariantCulture)
            : "finished";

        var immediately = exit.WasImmediateFailure
            ? " That was immediate, so the next start waits."
            : string.Empty;

        return "Worker " + exit.WorkerId + " " + how + " after "
               + Round(exit.Ran.TotalSeconds) + " seconds." + immediately;
    }

    private static string Describe(SwarmConnectionErrorEventArgs error)
    {
        if (error == null)
        {
            return "The Hive's connection reported something with no detail.";
        }

        return error.Error == null
            ? "The Hive's connection, while " + error.Context + ": the connection closed."
            : "The Hive's connection, while " + error.Context + ": " + error.Error.Message;
    }

    private static string Round(double value)
        => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

    private static string Mebibytes(long bytes)
        => (bytes / (1024L * 1024L)).ToString(CultureInfo.InvariantCulture);

    private void Report(string message)
    {
        var report = _options.Report;

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
            //Reporting is best effort; a reporter that throws must not end the Hive.
        }
    }
}
