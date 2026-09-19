using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;
using CodeBrix.Swarm.Hive;
using CodeBrix.Swarm.Hive.Spawning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.EndToEnd.Tests;

/// <summary>
/// The several ways a swarm comes to an end, every one of them with real processes: the coordinator ending
/// the Workers, the coordinator ending the whole thing, the coordinator disappearing, and a Worker left
/// holding a lifeline that has gone.
/// </summary>
public class TerminationScenarios
{
    [Fact]
    public async Task terminate_yourself_ends_the_workers_and_the_hive_carries_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("workers-told-to-end");

        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 5,
            "five Workers to reach the coordinator",
            cancellationToken);

        var theFirstFive = scenario.Processes.Started;

        //Act - every Worker is told to end itself. This is not the end of the work, so the Hive is free to
        //start more.
        await scenario.Queen.SendTerminateToWorkersAsync(cancellationToken);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Exits.Count >= 5,
            "all five Workers to end",
            cancellationToken);

        //Assert - they ended normally. Being told to end is not a failure.
        foreach (var exit in scenario.Exits)
        {
            exit.ExitCode.Should().Be(SwarmExitCodes.Success);
            exit.IsFailure.Should().BeFalse();
        }

        //And the Hive replaced them, because nothing told it the work was over.
        await SwarmScenario.WaitUntilAsync(
            () => scenario.Processes.Count > theFirstFive.Count,
            "the Hive to start replacements",
            cancellationToken);

        //Act
        scenario.EndTheHive();
        await scenario.WaitForHiveAsync();

        //Assert
        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task terminate_all_workers_ends_the_rest_and_the_hive_finishes_and_never_starts_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("the-work-is-over");

        var outcomeAtShutdown = default(SwarmHiveOutcome?);
        var runningAtShutdown = -1;

        options.ShutdownAsync = (context, _) =>
        {
            outcomeAtShutdown = context.Outcome;
            runningAtShutdown = context.RunningWorkerCount;
            return Task.CompletedTask;
        };

        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 5,
            "five Workers to reach the coordinator",
            cancellationToken);

        //Act - the work is over.
        await scenario.Queen.SendTerminateAllWorkersToHivesAsync(cancellationToken);

        var outcome = await scenario.WaitForHiveAsync();
        var askedByTheEnd = scenario.TimesAsked;

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.ToldToTerminate);

        //It told the application, and by then every Worker had gone.
        outcomeAtShutdown.Should().Be(SwarmHiveOutcome.ToldToTerminate);
        runningAtShutdown.Should().Be(0);

        //Every one of them ended normally: the end of the lifeline is a request, not a failure.
        scenario.Exits.Should().HaveCount(5);

        foreach (var exit in scenario.Exits)
        {
            exit.ExitCode.Should().Be(SwarmExitCodes.Success);
        }

        //Act - and nothing resumes. There is no message that undoes this one.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        //Assert
        scenario.TimesAsked.Should().Be(askedByTheEnd);
        scenario.Processes.Count.Should().Be(5);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 0,
            "the coordinator to notice that the Workers have gone",
            cancellationToken);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task the_coordinator_goes_away_and_the_hive_and_its_workers_end_by_themselves()
    {
        //Arrange - a short window on both sides, so that the rule runs its course in a couple of seconds
        //rather than a minute.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("coordinator-goes-away");
        options.Limits.MaxWorkers = 2;
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(2);

        scenario.NextWorker = (_, _) => Task.FromResult(
            WorkerLaunch.StartWithWork(
                SampleWorkerProgram.Path,
                [SampleWorkerProgram.QueenWindowArgument(1)],
                SampleWorkerProgram.RunsUntilEnded()));

        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 2,
            "both Workers to reach the coordinator",
            cancellationToken);

        //Act - the coordinator stops listening, and nothing else is done to anything.
        await scenario.Queen.StopAsync(cancellationToken);

        var outcome = await scenario.WaitForHiveAsync();

        //Assert - the Hive gave up by itself once its window ran out, and ended its Workers on the way.
        outcome.Should().Be(SwarmHiveOutcome.QueenUnreachable);
        scenario.Processes.Count.Should().Be(2);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_lone_worker_gives_up_by_itself_when_the_coordinator_never_answers()
    {
        //Arrange - the Worker's own side of the same rule, with no Hive involved at all: a real Worker
        //process, started by hand, pointed at an address nothing is listening on.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var byHand = SwarmScenario.WithNoCoordinator();

        var configuration = new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings
            {
                QueenUrl = byHand.QueenUrl,
                Token = "no-coordinator-to-mint-one",
                WorkerId = "lone-worker"
            },
            Work = "{\"label\":\"lonely\",\"steps\":0,\"stepMilliseconds\":100}"
        };

        //Act
        var exitCode = await WorkerByHand.RunToCompletionAsync(
            configuration,
            [SampleWorkerProgram.QueenWindowArgument(1)],
            cancellationToken);

        //Assert - it kept trying for its window and then said so in the one way a process can.
        exitCode.Should().Be(SwarmExitCodes.QueenUnreachable);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_worker_ends_by_itself_when_its_lifeline_closes()
    {
        //Arrange - THE LIFELINE. A Hive hands a Worker its configuration down a pipe and then leaves the
        //pipe open; while it is open the Hive is there. A Hive that is ended abruptly leaves that pipe's
        //other end closed and nothing else: no message, nothing stopped. What follows is what the Worker
        //makes of that, with the Hive's own spawning machinery starting a real process.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var configuration = new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings
            {
                QueenUrl = scenario.QueenUrl,
                Token = scenario.Queen.CreateWorkerToken(),
                WorkerId = "worker-on-a-lifeline"
            },
            Work = "{\"label\":\"endless\",\"steps\":0,\"stepMilliseconds\":100}"
        };

        var request = new WorkerProcessRequest(
            "worker-on-a-lifeline",
            SampleWorkerProgram.Path,
            [],
            null,
            configuration.ToJsonLine(),
            false,
            null);

        var worker = WorkerProcessFactory.Instance.Start(request);

        try
        {
            await SwarmScenario.WaitUntilAsync(
                () => scenario.Queen.WorkerCount == 1,
                "the Worker to reach the coordinator",
                cancellationToken);

            worker.HasExited.Should().BeFalse();

            //Act - the lifeline goes, and nothing else is done to the process at all.
            worker.CloseLifeline();

            await worker.WaitForExitAsync(cancellationToken)
                .WaitAsync(SwarmScenario.WaitLimit, cancellationToken);

            //Assert - it ended by itself, normally: the end of the lifeline is a request to exit, not a
            //failure, and nothing had to stop it.
            worker.HasExited.Should().BeTrue();
            worker.ExitCode.Should().Be(SwarmExitCodes.Success);
        }
        finally
        {
            worker.ForceStop();
            worker.Dispose();
        }

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }
}
