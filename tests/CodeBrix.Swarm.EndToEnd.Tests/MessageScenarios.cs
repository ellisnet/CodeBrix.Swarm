using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;
using CodeBrix.Swarm.Hive;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.EndToEnd.Tests;

/// <summary>
/// The generic channel, over a real coordinator: a message the application invented reaching a real Hive,
/// and the same thing reaching five real Worker processes at once.
/// </summary>
public class MessageScenarios
{
    /// <summary>
    /// The kind the sample Worker listens for. A coordinator may define as many kinds as it likes; the
    /// only rule is that one must not begin with the prefix the swarm keeps for its own.
    /// </summary>
    private const string NoteMessageKind = "sample.note";

    private sealed class Note
    {
        public string Text { get; set; }
    }

    [Fact]
    public async Task a_message_the_application_invented_reaches_the_hive()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("hive-gets-a-message");

        //Nothing to start: this scenario is about the message, not the Workers.
        scenario.NextWorker = (_, _) => Task.FromResult(WorkerLaunch.NotNow);

        var arrived = new TaskCompletionSource<SwarmMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        options.ConfigureAsync = (context, _) =>
        {
            context.RegisterHandler(NoteMessageKind, (message, _) =>
            {
                arrived.TrySetResult(message);
                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        };

        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.HiveCount == 1,
            "the Hive to reach the coordinator",
            cancellationToken);

        //Act
        await scenario.Queen.SendToHivesAsync(
            SwarmMessage.Create(NoteMessageKind, new Note { Text = "for every Hive" }),
            cancellationToken);

        var delivered = await arrived.Task.WaitAsync(SwarmScenario.WaitLimit, cancellationToken);

        //Assert
        delivered.Kind.Should().Be(NoteMessageKind);
        delivered.GetPayload<Note>().Text.Should().Be("for every Hive");

        //Act
        scenario.EndTheHive();
        await scenario.WaitForHiveAsync();

        //Assert
        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_message_the_application_invented_reaches_every_one_of_the_five_workers()
    {
        //Arrange - the Workers are asked to say what they receive, and the Hive to collect what they say.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var options = scenario.HiveOptions("workers-get-a-message");
        options.CaptureWorkerOutput = true;

        var work = SampleWorkerProgram.RunsUntilEndedAndReports();
        work.StepMilliseconds = 1000;

        scenario.NextWorker = (_, _) => Task.FromResult(
            WorkerLaunch.StartWithWork(SampleWorkerProgram.Path, [], work));

        scenario.StartHive(options);

        await SwarmScenario.WaitUntilAsync(
            () => scenario.Queen.WorkerCount == 5,
            "five Workers to reach the coordinator",
            cancellationToken);

        //Act
        await scenario.Queen.SendToWorkersAsync(
            SwarmMessage.Create(NoteMessageKind, new Note { Text = "for every Worker" }),
            cancellationToken);

        await SwarmScenario.WaitUntilAsync(
            () => WorkersThatSaidTheyGotANote(scenario).Count == 5,
            "all five Workers to say they received the note",
            cancellationToken);

        //Assert
        var receivers = WorkersThatSaidTheyGotANote(scenario);
        receivers.Should().HaveCount(5);

        foreach (var worker in scenario.Processes.Started)
        {
            receivers.Should().Contain(worker.WorkerId);
        }

        //Act
        scenario.EndTheHive();
        await scenario.WaitForHiveAsync();

        //Assert
        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task the_coordinator_refuses_to_send_one_of_the_swarms_own_kinds_by_hand()
    {
        //Arrange - starting and ending Workers is the swarm's own business, and an application that could
        //forge one of those messages could end a swarm's work by accident.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        //Act
        var sendToHives = async () => await scenario.Queen.SendToHivesAsync(
            SwarmMessage.Create(SwarmMessageKinds.TerminateAllWorkers), cancellationToken);

        var sendToWorkers = async () => await scenario.Queen.SendToWorkersAsync(
            SwarmMessage.Create(SwarmMessageKinds.TerminateWorker), cancellationToken);

        //Assert
        await sendToHives.Should().ThrowAsync<ArgumentException>();
        await sendToWorkers.Should().ThrowAsync<ArgumentException>();

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    private static IReadOnlyCollection<string> WorkersThatSaidTheyGotANote(SwarmScenario scenario)
    {
        var said = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in scenario.Output)
        {
            if (line.Text != null && line.Text.StartsWith("note ", StringComparison.Ordinal))
            {
                said.Add(line.WorkerId);
            }
        }

        return said;
    }
}
