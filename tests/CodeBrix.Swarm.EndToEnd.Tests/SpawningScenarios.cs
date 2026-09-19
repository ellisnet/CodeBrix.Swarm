using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;
using CodeBrix.Swarm.Hive;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.EndToEnd.Tests;

/// <summary>
/// A real coordinator, a real Hive and real Worker processes: what a Hive starts, when it starts it, how
/// many, and what each one is told.
/// </summary>
public class SpawningScenarios
{
    [Fact]
    public async Task the_hive_starts_nothing_before_it_has_reached_the_coordinator()
    {
        //Arrange - an address nothing is listening on. A Hive that started Workers before it knew where
        //its coordinator was would have no way of ending them.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = SwarmScenario.WithNoCoordinator();

        var options = scenario.HiveOptions("starts-nothing-first");
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(2);

        //Act
        scenario.StartHive(options);
        var outcome = await scenario.WaitForHiveAsync();

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.QueenUnreachable);
        scenario.Processes.Count.Should().Be(0);

        //It did not even ask what to start: there was no point until it had somewhere to report to.
        scenario.TimesAsked.Should().Be(0);
        scenario.Host.Reads.Should().Be(0);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task the_hive_starts_five_workers_and_no_more_and_the_coordinator_counts_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("five-and-no-more");
        options.Limits.MaxWorkers.Should().Be(5);

        //Act
        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 5,
            "five Workers to reach the coordinator",
            cancellationToken);

        //Several settle intervals' worth of chances to start a sixth.
        var startedASixth = await SwarmScenario.BecomesTrueAsync(
            () => scenario.Processes.Count > 5, TimeSpan.FromSeconds(2), cancellationToken);

        //Assert
        startedASixth.Should().BeFalse();
        scenario.Processes.Count.Should().Be(5);

        //And it stopped ASKING as well: there is no point troubling the application for something it
        //cannot be given.
        scenario.TimesAsked.Should().Be(5);

        scenario.Queen.HiveCount.Should().Be(1);
        scenario.Queen.WorkerCount.Should().Be(5);

        //Act - and it all winds down again.
        scenario.EndTheHive();
        var outcome = await scenario.WaitForHiveAsync();

        //Assert
        outcome.Should().Be(SwarmHiveOutcome.Cancelled);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 0,
            "the coordinator to notice that the Workers have gone",
            cancellationToken);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task every_worker_is_handed_its_configuration_and_the_application_part_arrives_intact()
    {
        //Arrange - the Workers are asked to say what they were handed, and the Hive is asked to collect
        //what they say, so the application's own piece of JSON can be followed all the way from the step
        //that made it to the process that read it.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("configuration-intact");
        options.CaptureWorkerOutput = true;

        var work = SampleWorkerProgram.RunsUntilEndedAndReports();
        work.Label = "a label with spaces, a \"quote\" and a comma";
        work.StepMilliseconds = 500;

        scenario.NextWorker = (_, _) => Task.FromResult(
            WorkerLaunch.StartWithWork(SampleWorkerProgram.Path, ["--mode=long"], work));

        //Act
        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 5,
            "five Workers to reach the coordinator",
            cancellationToken);

        await SwarmScenario.WaitUntilAsync(
            () => CountOfWorkersThatSaidWhatTheyWereHanded(scenario) == 5,
            "all five Workers to say what they were handed",
            cancellationToken);

        //Assert
        var started = scenario.Processes.Started;
        started.Should().HaveCount(5);

        var names = new List<string>();

        foreach (var worker in started)
        {
            worker.Request.Arguments.Should().ContainInOrder("--mode=long");

            //The swarm's own part: where the coordinator is, the token for the Worker hub, and a name of
            //this Worker's own.
            worker.Configuration.Swarm.QueenUrl.Should().Be(scenario.QueenUrl);
            worker.Configuration.Swarm.Token.Should().Be(options.WorkerToken);
            worker.Configuration.Swarm.Token.Should().NotBe(options.HiveToken);
            worker.Configuration.Swarm.WorkerId.Should().Be(worker.WorkerId);
            worker.WorkerId.Should().StartWith("configuration-intact");

            names.Should().NotContain(worker.WorkerId);
            names.Add(worker.WorkerId);

            //One line, so that the Worker reads it with one read and the pipe then stays open as the
            //lifeline.
            worker.Request.ConfigurationJsonLine.Should().NotContain("\n");

            //THE APPLICATION'S PART, AS THE WORKER ITSELF READ IT. The Worker writes out the text it was
            //given; the swarm neither read it nor rewrote it on the way.
            var asTheWorkerReadIt = WhatTheWorkerSaidItWasHanded(scenario, worker.WorkerId);
            asTheWorkerReadIt.Should().Be(worker.Configuration.Work);

            //And it still means what it meant: the awkward characters in the label came through.
            var readBack = JsonSerializer.Deserialize<SampleWork>(
                asTheWorkerReadIt,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            readBack.Label.Should().Be(work.Label);
            readBack.StepMilliseconds.Should().Be(500);
        }

        //Act
        scenario.EndTheHive();
        await scenario.WaitForHiveAsync();

        //Assert
        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    private static int CountOfWorkersThatSaidWhatTheyWereHanded(SwarmScenario scenario)
    {
        var said = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in scenario.Output)
        {
            if (line.Text != null && line.Text.StartsWith("work ", StringComparison.Ordinal))
            {
                said.Add(line.WorkerId);
            }
        }

        return said.Count;
    }

    private static string WhatTheWorkerSaidItWasHanded(SwarmScenario scenario, string workerId)
    {
        foreach (var line in scenario.Output)
        {
            if (string.Equals(line.WorkerId, workerId, StringComparison.Ordinal)
                && line.Text != null
                && line.Text.StartsWith("work ", StringComparison.Ordinal))
            {
                return line.Text["work ".Length..];
            }
        }

        return null;
    }
}
