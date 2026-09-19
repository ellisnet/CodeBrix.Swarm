using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;
using CodeBrix.Swarm.Hive;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.EndToEnd.Tests;

/// <summary>
/// What a Hive does about a Worker that ends: notices it, tells the application, and decides whether to
/// start another one straight away or to wait first. Real processes, really ending.
/// </summary>
public class WorkerEndingScenarios
{
    [Fact]
    public async Task a_worker_that_finishes_is_noticed_and_replaced_without_any_waiting()
    {
        //Arrange - one Worker at a time, and work that finishes almost as soon as it starts. Finishing is
        //not a failure however quick it was, so nothing should hold the next one back.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("finishes-and-is-replaced");
        options.Limits.MaxWorkers = 1;

        scenario.NextWorker = (_, _) => Task.FromResult(
            WorkerLaunch.StartWithWork(
                SampleWorkerProgram.Path, [], SampleWorkerProgram.FinishesAtOnce()));

        //Act
        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Exits.Count >= 3,
            "three Workers to finish and be replaced",
            cancellationToken);

        //Assert
        var exits = scenario.Exits;

        foreach (var exit in exits)
        {
            exit.ExitCode.Should().Be(SwarmExitCodes.Success);
            exit.IsFailure.Should().BeFalse();
            exit.WasImmediateFailure.Should().BeFalse();
            exit.ProcessId.Should().BeGreaterThan(0);
            exit.WorkerId.Should().StartWith("finishes-and-is-replaced");
        }

        var started = scenario.Processes.Started;
        started.Count.Should().BeGreaterThanOrEqualTo(3);

        //No wait after a failure came into it: each replacement followed on from the one before it.
        (started[1].StartedUtc - started[0].StartedUtc).Should().BeLessThan(options.FirstBackoff);
        (started[2].StartedUtc - started[1].StartedUtc).Should().BeLessThan(options.FirstBackoff);

        //Act
        scenario.EndTheHive();
        await scenario.WaitForHiveAsync();

        //Assert
        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_worker_that_fails_at_once_makes_the_hive_wait_before_starting_another()
    {
        //Arrange - work that throws before it has done anything, and a long wait after the first such
        //failure. Starting Worker after Worker that cannot survive its first seconds achieves nothing.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("fails-at-once");
        options.Limits.MaxWorkers = 1;
        options.FirstBackoff = TimeSpan.FromSeconds(20);
        options.MaximumBackoff = TimeSpan.FromMinutes(1);

        scenario.NextWorker = (_, _) => Task.FromResult(
            WorkerLaunch.StartWithWork(
                SampleWorkerProgram.Path, [], SampleWorkerProgram.FailsAtOnce()));

        //Act
        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Exits.Count >= 1,
            "the first Worker to fail",
            cancellationToken);

        //Assert
        var failure = scenario.Exits[0];
        failure.ExitCode.Should().Be(SwarmExitCodes.WorkFailed);
        failure.IsFailure.Should().BeTrue();
        failure.WasImmediateFailure.Should().BeTrue();

        //Act - and now nothing more happens for a while, because the Hive is waiting.
        var startedAnother = await SwarmScenario.BecomesTrueAsync(
            () => scenario.Processes.Count > 1, TimeSpan.FromSeconds(3), cancellationToken);

        //Assert
        startedAnother.Should().BeFalse();
        scenario.Processes.Count.Should().Be(1);

        //Act
        scenario.EndTheHive();
        var outcome = await scenario.WaitForHiveAsync();

        //Assert - a Hive that is waiting is still a Hive that ends when it is told to.
        outcome.Should().Be(SwarmHiveOutcome.Cancelled);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task not_now_pauses_starting_and_is_never_treated_as_a_failure()
    {
        //Arrange - the application has nothing to hand out for a while, and then does. A long wait after
        //failures is set up on purpose: if "not now" were being treated as one, the Worker that follows
        //would not appear for twenty seconds.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("not-now-then-yes");
        options.Limits.MaxWorkers = 1;
        options.FirstBackoff = TimeSpan.FromSeconds(20);
        options.MaximumBackoff = TimeSpan.FromMinutes(1);

        var readyToHandOutWork = 0;

        scenario.NextWorker = (_, _) =>
        {
            if (Volatile.Read(ref readyToHandOutWork) == 0)
            {
                return Task.FromResult(WorkerLaunch.NotNow);
            }

            return Task.FromResult(WorkerLaunch.StartWithWork(
                SampleWorkerProgram.Path, [], SampleWorkerProgram.RunsUntilEnded()));
        };

        //Act
        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.TimesAsked >= 5,
            "the Hive to ask what to start several times",
            cancellationToken);

        //Assert - it asked again and again, and started nothing.
        scenario.Processes.Count.Should().Be(0);

        //Act - and now there is something to hand out.
        var readyAt = DateTime.UtcNow;
        Volatile.Write(ref readyToHandOutWork, 1);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Processes.Count == 1,
            "the Hive to start the Worker it was finally given",
            cancellationToken);

        //Assert - straight away, not after a wait: nothing counted the refusals as failures.
        var startedAfter = scenario.Processes.Started[0].StartedUtc - readyAt;
        startedAfter.Should().BeLessThan(TimeSpan.FromSeconds(5));

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 1,
            "that Worker to reach the coordinator",
            cancellationToken);

        //Act
        scenario.EndTheHive();
        await scenario.WaitForHiveAsync();

        //Assert
        scenario.Exits.Should().HaveCount(1);
        scenario.Exits[0].ExitCode.Should().Be(SwarmExitCodes.Success);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }
}
