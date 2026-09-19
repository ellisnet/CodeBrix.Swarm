using System;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.EndToEnd.Tests;

/// <summary>
/// A real Worker process with no Hive anywhere: the same configuration in a file, named on the command
/// line. This is how a Worker is debugged, and it has to keep working, because the alternative is that
/// nobody can look at one without a whole swarm round it.
/// </summary>
public class DevelopmentModeScenarios
{
    [Fact]
    public async Task a_worker_started_from_a_configuration_file_runs_without_a_hive()
    {
        //Arrange - a real coordinator, and a Worker started by hand against it.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var configuration = new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings
            {
                QueenUrl = scenario.QueenUrl,
                Token = scenario.Queen.CreateWorkerToken(),
                WorkerId = "started-by-hand"
            },

            //Three quick steps and then finished, and saying so as it goes.
            Work = "{\"label\":\"by hand\",\"steps\":3,\"stepMilliseconds\":50,\"report\":true}"
        };

        //Act
        var result = await WorkerByHand.RunAsync(configuration, [], cancellationToken);

        //Assert - it ran its work to the end and exited normally, with no Hive and no lifeline.
        result.ExitCode.Should().Be(SwarmExitCodes.Success);
        result.Lines.Should().Contain("developmentMode true");
        result.Lines.Should().Contain("workerId started-by-hand");
        result.Lines.Should().Contain("step 3 of by hand");
        result.Lines.Should().Contain("finished after 3 step(s)");
        result.Lines.Should().Contain("ending because WorkFinished");

        //And it really did subscribe: the coordinator saw it come and go.
        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 0,
            "the coordinator to notice the Worker has gone",
            cancellationToken);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_worker_started_by_hand_refuses_a_configuration_it_cannot_use()
    {
        //Arrange - the swarm's own part is missing altogether, so the Worker has nowhere to report to and
        //no way of being let in.
        var cancellationToken = TestContext.Current.CancellationToken;

        var configuration = new WorkerConfiguration
        {
            Work = "{\"label\":\"nowhere to go\"}"
        };

        //Act
        var exitCode = await WorkerByHand.RunToCompletionAsync(configuration, [], cancellationToken);

        //Assert - the exit codes are part of the contract, and this is the one that says so.
        exitCode.Should().Be(SwarmExitCodes.ConfigurationInvalid);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_worker_started_by_hand_reports_work_that_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var configuration = new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings
            {
                QueenUrl = scenario.QueenUrl,
                Token = scenario.Queen.CreateWorkerToken(),
                WorkerId = "fails-by-hand"
            },
            Work = "{\"label\":\"hopeless\",\"steps\":1,\"stepMilliseconds\":0,\"failAtStep\":0,\"report\":true}"
        };

        //Act
        var result = await WorkerByHand.RunAsync(configuration, [], cancellationToken);

        //Assert
        result.ExitCode.Should().Be(SwarmExitCodes.WorkFailed);
        result.Lines.Should().Contain("ending because WorkFailed");

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }
}
