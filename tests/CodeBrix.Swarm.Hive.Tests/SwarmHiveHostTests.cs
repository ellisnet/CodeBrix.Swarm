using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.Hive.HostLoad;
using CodeBrix.Swarm.Hive.Spawning;
using CodeBrix.Swarm.Hive.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests;

/// <summary>
/// The whole of a Hive's decision-making, run against a substituted host, substituted processes, a
/// substituted connection and a clock that never sleeps. A run whose intervals are measured in minutes
/// therefore finishes in microseconds, and the pacing, the limits and the waits after failures are all
/// observable in what the substitutes were asked to do and when.
/// </summary>
/// <remarks>
/// Under a clock that does not sleep the loop never pauses, so a test reaches into it through a call it
/// makes: the substituted host is read on every pass, and its reading hook is where a test delivers a
/// message or ends the run.
/// </remarks>
public class SwarmHiveHostTests
{
    private const string QueenUrl = "http://127.0.0.1:5000";

    private const string HiveToken = "a-token-for-this-hive";

    private const string WorkerToken = "a-token-for-its-workers";

    private const string HiveId = "host-under-test";

    private const string WorkerExecutable = "/opt/work/worker";

    private static SwarmHiveOptions Options() => new()
    {
        QueenUrl = QueenUrl,
        HiveToken = HiveToken,
        WorkerToken = WorkerToken,
        HiveId = HiveId,
        NextWorkerAsync = (_, _) => Task.FromResult(WorkerLaunch.Start(WorkerExecutable)),
        PollInterval = TimeSpan.FromMilliseconds(250),
        SettleInterval = TimeSpan.FromSeconds(1),
        NotNowInterval = TimeSpan.FromSeconds(2),
        FirstBackoff = TimeSpan.FromSeconds(5),
        MaximumBackoff = TimeSpan.FromMinutes(5),
        ImmediateFailureWindow = TimeSpan.FromSeconds(10),
        BackoffResetAfter = TimeSpan.FromMinutes(1),
        TerminationGracePeriod = TimeSpan.FromSeconds(10)
    };

    private static Task<SwarmHiveOutcome> RunAsync(
        SwarmHiveOptions options,
        IHostLoadProbe probe,
        IWorkerProcessFactory processes,
        ISwarmHubConnectionFactory connections,
        FakeSwarmClock clock,
        CancellationToken cancellationToken)
        => SwarmHiveHost.RunAsync(
            options,
            options.Limits.Copy(),
            options.HiveId,
            probe,
            processes,
            connections,
            clock,
            cancellationToken);

    /// <summary>
    /// Ends the run once the substituted host has been read this many times. Every test that would
    /// otherwise run for ever uses this, and it doubles as the safety net for one that goes wrong.
    /// </summary>
    private static Action<int> StopAfterReads(int reads, CancellationTokenSource stopping)
        => read =>
        {
            if (read >= reads)
            {
                stopping.Cancel();
            }
        };

    [Fact]
    public async Task RunAsync_refuses_options_that_cannot_be_used()
    {
        //Arrange
        var options = Options();
        options.WorkerToken = null;

        var clock = new FakeSwarmClock();

        var run = async () => await SwarmHiveHost.RunAsync(
            options,
            new FakeHostLoadProbe(),
            new FakeWorkerProcessFactory(clock),
            new FakeHubConnectionFactory(),
            clock,
            TestContext.Current.CancellationToken);

        //Act, Assert
        await run.Should().ThrowAsync<SwarmConfigurationException>();
    }

    [Fact]
    public async Task RunAsync_starts_nothing_at_all_before_it_has_reached_the_coordinator()
    {
        //Arrange - every attempt to reach the coordinator is refused, for the whole window.
        var options = Options();
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(60);

        var asked = 0;
        options.NextWorkerAsync = (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult(WorkerLaunch.Start(WorkerExecutable));
        };

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe();
        var connections = new FakeHubConnectionFactory(_ => true);

        //Act
        var outcome = await RunAsync(
            options, probe, processes, connections, clock, TestContext.Current.CancellationToken);

        //Assert - a Hive that had started Workers before it knew where its coordinator was would have no
        //way of ending them.
        outcome.Should().Be(SwarmHiveOutcome.QueenUnreachable);
        processes.Started.Should().BeEmpty();
        asked.Should().Be(0);
        probe.Reads.Should().Be(0);
        connections.Created.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task RunAsync_starts_one_worker_at_a_time_with_the_settle_interval_between_them()
    {
        //Arrange
        var options = Options();
        options.Limits.MaxWorkers = 3;

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(12, stopping) };

        //Act
        var outcome = await RunAsync(
            options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.Cancelled);

        var started = processes.Started;
        started.Should().HaveCount(3);

        //A Worker that has only just started has not yet taken up the memory it is going to, so the
        //next decision waits for it to settle.
        (started[1].StartedUtc - started[0].StartedUtc).Should().Be(options.SettleInterval);
        (started[2].StartedUtc - started[1].StartedUtc).Should().Be(options.SettleInterval);
    }

    [Fact]
    public async Task RunAsync_never_starts_more_workers_than_it_is_allowed()
    {
        //Arrange - the host has plenty of room, so the only thing holding the Hive back is the limit.
        var options = Options();
        options.Limits.MaxWorkers = 5;

        var asked = 0;
        options.NextWorkerAsync = (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult(WorkerLaunch.Start(WorkerExecutable));
        };

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(40, stopping) };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert - and it stops ASKING as well as stopping starting: there is no point troubling the
        //application for something it cannot be given.
        processes.Started.Should().HaveCount(5);
        asked.Should().Be(5);
    }

    [Fact]
    public async Task RunAsync_hands_every_worker_the_configuration_it_needs()
    {
        //Arrange
        const string work = "{\"piece\":\"seventeen\",\"count\":3}";

        var options = Options();
        options.Limits.MaxWorkers = 2;
        options.NextWorkerAsync = (_, _) =>
            Task.FromResult(WorkerLaunch.Start(WorkerExecutable, ["--mode=long"], work));

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(8, stopping) };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        var started = processes.Started;
        started.Should().HaveCount(2);

        var names = new List<string>();

        foreach (var worker in started)
        {
            worker.Request.Executable.Should().Be(WorkerExecutable);
            worker.Request.Arguments.Should().ContainInOrder("--mode=long");

            var configuration = WorkerConfiguration.Parse(worker.Request.ConfigurationJsonLine);
            configuration.Swarm.QueenUrl.Should().Be(QueenUrl);

            //Passed on exactly as it arrived, and never this Hive's own token.
            configuration.Swarm.Token.Should().Be(WorkerToken);
            configuration.Swarm.Token.Should().NotBe(HiveToken);

            configuration.Swarm.WorkerId.Should().StartWith(HiveId);

            //The application's part is carried through without being read or rewritten.
            configuration.Work.Should().Be(work);

            names.Add(configuration.Swarm.WorkerId);
        }

        names[0].Should().NotBe(names[1]);

        //One line, so that the Worker reads it with one read and the pipe then stays open as the
        //lifeline.
        started[0].Request.ConfigurationJsonLine.Should().NotContain("\n");
    }

    [Fact]
    public async Task RunAsync_starts_nothing_while_the_host_has_no_room()
    {
        //Arrange
        var options = Options();

        var asked = 0;
        options.NextWorkerAsync = (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult(WorkerLaunch.Start(WorkerExecutable));
        };

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe(FakeHostLoadProbe.FullHost())
        {
            OnRead = StopAfterReads(10, stopping)
        };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        processes.Started.Should().BeEmpty();
        asked.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_starts_nothing_when_the_host_cannot_be_measured_at_all()
    {
        //Arrange
        var options = Options();

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe(HostLoadReading.Unknown)
        {
            OnRead = StopAfterReads(10, stopping)
        };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert - a Hive that cannot measure its host does not start Workers on it.
        processes.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_stops_starting_when_another_worker_would_take_the_host_past_its_share()
    {
        //Arrange - sixteen gibibytes altogether with eight available to begin with, Workers that turn
        //out to need one and a half each, and a floor of one. The host's available memory goes down as
        //the Workers take their share, which is what a real host does.
        const long gibibyte = 1024L * 1024L * 1024L;
        const long footprint = 3L * gibibyte / 2L;

        var options = Options();
        options.Limits.FreeRamFloorBytes = gibibyte;

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock) { WorkingSetBytes = footprint };

        using var stopping = new CancellationTokenSource();

        var probe = new FakeHostLoadProbe(_ => new HostLoadReading(
            16L * gibibyte,
            (8L * gibibyte) - (processes.Started.Count * footprint),
            0d))
        {
            OnRead = StopAfterReads(30, stopping)
        };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert - four Workers, and then it stops. At that point the host still has two gibibytes
        //available and is only at 87 percent in use, so a Hive that judged the host AS IT IS would have
        //started a fifth and left half a gibibyte. Judging it as it WOULD BE once another Worker had
        //taken the share its predecessors turned out to need is what stops at four.
        processes.Started.Should().HaveCount(4);
    }

    [Fact]
    public async Task RunAsync_keeps_asking_after_not_now_and_never_counts_it_against_anything()
    {
        //Arrange
        var options = Options();

        using var stopping = new CancellationTokenSource();
        var asked = 0;

        options.NextWorkerAsync = (_, _) =>
        {
            if (Interlocked.Increment(ref asked) >= 5)
            {
                stopping.Cancel();
            }

            return Task.FromResult(WorkerLaunch.NotNow);
        };

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(1000, stopping) };

        //Act
        var outcome = await RunAsync(
            options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.Cancelled);
        asked.Should().Be(5);
        processes.Started.Should().BeEmpty();

        //Every wait was the ordinary "ask again shortly" one. Nothing treated "not now" as a failure,
        //so none of the waits after a failure ever came into it.
        clock.Delays.Should().HaveCount(4);

        foreach (var delay in clock.Delays)
        {
            delay.Should().Be(options.NotNowInterval);
        }
    }

    [Fact]
    public async Task RunAsync_waits_longer_after_each_worker_that_fails_the_instant_it_starts()
    {
        //Arrange - every Worker fails as soon as it is started, the way one with a configuration its
        //work cannot use would.
        var options = Options();

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();

        var processes = new FakeWorkerProcessFactory(clock);

        processes.OnStarted = worker =>
        {
            worker.Exit(SwarmExitCodes.WorkFailed);

            if (processes.Started.Count >= 4)
            {
                stopping.Cancel();
            }
        };

        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(5000, stopping) };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        var started = processes.Started;
        started.Count.Should().BeGreaterThanOrEqualTo(3);

        var firstGap = started[1].StartedUtc - started[0].StartedUtc;
        var secondGap = started[2].StartedUtc - started[1].StartedUtc;

        firstGap.Should().BeGreaterThanOrEqualTo(options.FirstBackoff);
        secondGap.Should().BeGreaterThanOrEqualTo(options.FirstBackoff + options.FirstBackoff);
        secondGap.Should().BeGreaterThan(firstGap);
    }

    [Fact]
    public async Task RunAsync_replaces_a_worker_that_finished_without_waiting_at_all()
    {
        //Arrange - every Worker finishes its work at once, which is not a failure however quick it was.
        var options = Options();

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var exits = new List<WorkerExit>();

        options.WorkerExited = exit =>
        {
            exits.Add(exit);

            if (exits.Count >= 3)
            {
                stopping.Cancel();
            }
        };

        var processes = new FakeWorkerProcessFactory(clock)
        {
            OnStarted = worker => worker.Exit(SwarmExitCodes.Success)
        };

        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(5000, stopping) };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        exits.Should().HaveCount(3);

        foreach (var exit in exits)
        {
            exit.ExitCode.Should().Be(SwarmExitCodes.Success);
            exit.IsFailure.Should().BeFalse();
            exit.WasImmediateFailure.Should().BeFalse();
            exit.WorkerId.Should().StartWith(HiveId);
        }

        var started = processes.Started;

        //No wait after a failure came into it: each replacement followed the settle interval alone.
        (started[1].StartedUtc - started[0].StartedUtc).Should().BeLessThan(options.FirstBackoff);
    }

    [Fact]
    public async Task RunAsync_waits_before_trying_again_when_a_worker_cannot_be_started_at_all()
    {
        //Arrange - the program named is not there, so the next attempt would fail the same way at once.
        var options = Options();

        using var stopping = new CancellationTokenSource();
        var asked = 0;
        var askedAt = new List<DateTime>();
        var clock = new FakeSwarmClock();

        options.NextWorkerAsync = (_, _) =>
        {
            askedAt.Add(clock.UtcNow);

            if (Interlocked.Increment(ref asked) >= 3)
            {
                stopping.Cancel();
            }

            return Task.FromResult(WorkerLaunch.Start(WorkerExecutable));
        };

        var processes = new FakeWorkerProcessFactory(clock) { RefuseToStart = true };
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(5000, stopping) };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        processes.Started.Should().BeEmpty();
        askedAt.Should().HaveCount(3);
        (askedAt[1] - askedAt[0]).Should().BeGreaterThanOrEqualTo(options.FirstBackoff);
    }

    [Fact]
    public async Task RunAsync_ends_every_worker_and_finishes_when_the_coordinator_says_the_work_is_over()
    {
        //Arrange
        var options = Options();
        options.Limits.MaxWorkers = 2;

        var asked = 0;
        options.NextWorkerAsync = (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult(WorkerLaunch.Start(WorkerExecutable));
        };

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var connections = new FakeHubConnectionFactory();

        var probe = new FakeHostLoadProbe
        {
            OnRead = read =>
            {
                //Both Workers are running by the fifth reading; then the coordinator says the work is
                //over.
                if (read == 5)
                {
                    _ = connections.Latest.DeliverAsync(
                        SwarmMessage.Create(SwarmMessageKinds.TerminateAllWorkers));
                }
            }
        };

        //Act
        var outcome = await RunAsync(
            options, probe, processes, connections, clock, TestContext.Current.CancellationToken);

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.ToldToTerminate);

        var started = processes.Started;
        started.Should().HaveCount(2);

        foreach (var worker in started)
        {
            //Politely first: the end of the lifeline is how a Worker is asked to wind itself down.
            worker.IsLifelineClosed.Should().BeTrue();
            worker.WasForceStopped.Should().BeFalse();
            worker.HasExited.Should().BeTrue();
            worker.IsDisposed.Should().BeTrue();
        }

        //Asked twice, for the two Workers it had room for, and never again: nothing resumes starting
        //Workers, and there is no message that undoes this one.
        asked.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_stops_a_worker_that_will_not_end_once_the_grace_period_has_run_out()
    {
        //Arrange - a Worker that ignores the end of its lifeline.
        var options = Options();
        options.Limits.MaxWorkers = 1;

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock)
        {
            OnStarted = worker => worker.EndsWhenLifelineCloses = false
        };

        var connections = new FakeHubConnectionFactory();

        var probe = new FakeHostLoadProbe
        {
            OnRead = read =>
            {
                if (read == 3)
                {
                    _ = connections.Latest.DeliverAsync(
                        SwarmMessage.Create(SwarmMessageKinds.TerminateAllWorkers));
                }
            }
        };

        var startedAt = clock.UtcNow;

        //Act
        var outcome = await RunAsync(
            options, probe, processes, connections, clock, TestContext.Current.CancellationToken);

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.ToldToTerminate);

        var worker = processes.Latest;
        worker.IsLifelineClosed.Should().BeTrue();
        worker.WasForceStopped.Should().BeTrue();
        worker.HasExited.Should().BeTrue();

        //It was given its grace period first.
        (clock.UtcNow - startedAt).Should().BeGreaterThanOrEqualTo(options.TerminationGracePeriod);
    }

    [Fact]
    public async Task RunAsync_finishes_as_failed_when_the_step_that_says_what_to_start_throws()
    {
        //Arrange
        var options = Options();
        options.NextWorkerAsync = (_, _) => throw new InvalidOperationException("nothing to hand out");

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);

        //Act
        var outcome = await RunAsync(
            options,
            new FakeHostLoadProbe(),
            processes,
            new FakeHubConnectionFactory(),
            clock,
            TestContext.Current.CancellationToken);

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.Failed);
        processes.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_lets_the_application_register_its_handlers_before_it_connects()
    {
        //Arrange
        var options = Options();
        options.Limits.MaxWorkers = 1;

        var connections = new FakeHubConnectionFactory();
        var connectionsWhenConfigured = -1;
        var noteArrived = 0;

        options.ConfigureAsync = (context, _) =>
        {
            connectionsWhenConfigured = connections.Created.Count;

            context.RegisterHandler("app.note", (_, _) =>
            {
                Interlocked.Increment(ref noteArrived);
                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        };

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();

        var probe = new FakeHostLoadProbe
        {
            OnRead = read =>
            {
                if (read == 3)
                {
                    _ = connections.Latest.DeliverAsync(SwarmMessage.Create("app.note"));
                }

                if (read >= 6)
                {
                    stopping.Cancel();
                }
            }
        };

        //Act
        await RunAsync(
            options, probe, new FakeWorkerProcessFactory(clock), connections, clock, stopping.Token);

        //Assert - registered before anything was connected, so nothing sent in the first moments is
        //missed.
        connectionsWhenConfigured.Should().Be(0);
        noteArrived.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_refuses_to_let_the_application_handle_one_of_the_swarms_own_kinds()
    {
        //Arrange
        var options = Options();
        Exception refused = null;

        options.ConfigureAsync = (context, _) =>
        {
            try
            {
                context.RegisterHandler(SwarmMessageKinds.TerminateAllWorkers, (_, _) => Task.CompletedTask);
            }
            catch (Exception ex)
            {
                refused = ex;
            }

            return Task.CompletedTask;
        };

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(2, stopping) };

        //Act
        await RunAsync(
            options,
            probe,
            new FakeWorkerProcessFactory(clock),
            new FakeHubConnectionFactory(),
            clock,
            stopping.Token);

        //Assert - the library already handles it, and a second answer to it would be a surprise.
        refused.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_runs_the_shutdown_step_after_the_last_worker_has_gone()
    {
        //Arrange
        var options = Options();
        options.Limits.MaxWorkers = 2;

        var outcomeAtShutdown = default(SwarmHiveOutcome?);
        var runningAtShutdown = -1;

        options.ShutdownAsync = (context, cancellationToken) =>
        {
            outcomeAtShutdown = context.Outcome;
            runningAtShutdown = context.RunningWorkerCount;

            //The last chance to put things down tidily, so the token is not a cancelled one.
            cancellationToken.IsCancellationRequested.Should().BeFalse();

            return Task.CompletedTask;
        };

        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var connections = new FakeHubConnectionFactory();

        var probe = new FakeHostLoadProbe
        {
            OnRead = read =>
            {
                if (read == 5)
                {
                    _ = connections.Latest.DeliverAsync(
                        SwarmMessage.Create(SwarmMessageKinds.TerminateAllWorkers));
                }
            }
        };

        //Act
        var outcome = await RunAsync(
            options, probe, processes, connections, clock, TestContext.Current.CancellationToken);

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.ToldToTerminate);
        outcomeAtShutdown.Should().Be(SwarmHiveOutcome.ToldToTerminate);
        runningAtShutdown.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ends_its_workers_when_something_outside_the_swarm_ends_the_run()
    {
        //Arrange
        var options = Options();
        options.Limits.MaxWorkers = 2;

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(5, stopping) };

        //Act
        var outcome = await RunAsync(
            options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert - exactly the same orderly ending as the coordinator's own message produces.
        outcome.Should().Be(SwarmHiveOutcome.Cancelled);
        processes.Started.Should().HaveCount(2);

        foreach (var worker in processes.Started)
        {
            worker.IsLifelineClosed.Should().BeTrue();
            worker.HasExited.Should().BeTrue();
            worker.IsDisposed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RunAsync_tells_the_application_what_it_did_through_the_reporting_it_was_given()
    {
        //Arrange
        var options = Options();
        options.Limits.MaxWorkers = 1;

        var lines = new List<string>();
        options.Report = line => lines.Add(line);

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(6, stopping) };

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        //Assert
        lines.Should().NotBeEmpty();
        var started = processes.Latest;
        lines.Exists(line => line.Contains(started.WorkerId, StringComparison.Ordinal))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task RunAsync_finishes_in_no_time_at_all_under_a_clock_that_does_not_sleep()
    {
        //Arrange - the whole point of every wait going through the clock. This run covers more than a
        //minute of a Hive's life.
        var options = Options();
        options.Limits.MaxWorkers = 3;

        using var stopping = new CancellationTokenSource();
        var clock = new FakeSwarmClock();
        var processes = new FakeWorkerProcessFactory(clock);
        var probe = new FakeHostLoadProbe { OnRead = StopAfterReads(100, stopping) };

        var startedAt = clock.UtcNow;
        var watch = Stopwatch.StartNew();

        //Act
        await RunAsync(options, probe, processes, new FakeHubConnectionFactory(), clock, stopping.Token);

        watch.Stop();

        //Assert
        (clock.UtcNow - startedAt).Should().BeGreaterThan(TimeSpan.FromMinutes(1));
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
