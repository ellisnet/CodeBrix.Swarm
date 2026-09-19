using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Messaging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Queen.Tests;

/// <summary>
/// A real coordinator on a real free port, with real hub connections in the same process. These are
/// the checks that nothing about the whole arrangement - the web host the library owns, the two
/// hubs, the authentication scheme, the role policies and the shared connection logic - is only
/// correct in principle.
/// </summary>
public class QueenWorkerConnectionSmoke
{
    private const string MasterSecret = "a-master-secret-long-enough-for-a-swarm";

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private sealed class Greeting
    {
        public string Note { get; set; }
    }

    private static Task<SwarmQueen> StartQueenAsync(CancellationToken cancellationToken)
        => SwarmQueen.StartAsync(
            new SwarmQueenOptions
            {
                Url = SwarmQueenOptions.AnyFreeLoopbackPortUrl,
                MasterSecret = MasterSecret
            },
            cancellationToken);

    private static SwarmConnectionOptions ConnectionTo(string baseUrl, string token, TimeSpan window)
        => new()
        {
            QueenUrl = baseUrl,
            Token = token,
            QueenUnreachableWindow = window,
            FirstRetryDelay = TimeSpan.FromMilliseconds(100),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(200)
        };

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + WaitLimit;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    [Fact]
    public async Task a_worker_token_is_accepted_at_the_worker_hub_and_a_message_is_delivered()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        queen.BaseUrl.Should().StartWith("http://127.0.0.1:");
        queen.BaseUrl.Should().NotEndWith(":0");

        await using var worker = new SwarmHubClient(
            SwarmRole.Worker,
            ConnectionTo(queen.BaseUrl, queen.CreateWorkerToken(), TimeSpan.FromSeconds(10)));

        var arrived = new TaskCompletionSource<SwarmMessage>();
        worker.Messages.Register("app.greeting", (message, _) =>
        {
            arrived.TrySetResult(message);
            return Task.CompletedTask;
        });

        //Act
        var connected = await worker.StartAsync(cancellationToken);
        await WaitUntilAsync(() => queen.WorkerCount == 1, cancellationToken);

        await queen.SendToWorkersAsync(
            SwarmMessage.Create("app.greeting", new Greeting { Note = "the work is yours" }),
            cancellationToken);

        var delivered = await arrived.Task.WaitAsync(WaitLimit, cancellationToken);

        //Assert
        connected.Should().BeTrue();
        queen.WorkerCount.Should().Be(1);
        queen.HiveCount.Should().Be(0);
        delivered.Kind.Should().Be("app.greeting");
        delivered.GetPayload<Greeting>().Note.Should().Be("the work is yours");

        await worker.StopAsync(cancellationToken);
        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task a_hive_token_is_accepted_at_the_hive_hub_and_a_message_is_delivered()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        await using var hive = new SwarmHubClient(
            SwarmRole.Hive,
            ConnectionTo(queen.BaseUrl, queen.CreateHiveToken(), TimeSpan.FromSeconds(10)));

        var toldToEnd = new TaskCompletionSource();
        hive.OnTerminate(_ =>
        {
            toldToEnd.TrySetResult();
            return Task.CompletedTask;
        });

        //Act
        var connected = await hive.StartAsync(cancellationToken);
        await WaitUntilAsync(() => queen.HiveCount == 1, cancellationToken);
        await queen.SendTerminateAllWorkersToHivesAsync(cancellationToken);
        await toldToEnd.Task.WaitAsync(WaitLimit, cancellationToken);

        //Assert
        connected.Should().BeTrue();
        queen.HiveCount.Should().Be(1);
        queen.WorkerCount.Should().Be(0);

        await hive.StopAsync(cancellationToken);
        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task a_hive_token_is_refused_at_the_worker_hub()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        await using var wrongRole = new SwarmHubClient(
            SwarmRole.Worker,
            ConnectionTo(queen.BaseUrl, queen.CreateHiveToken(), TimeSpan.FromSeconds(1)));

        //Act
        var connected = await wrongRole.StartAsync(cancellationToken);

        //Assert
        connected.Should().BeFalse();
        queen.WorkerCount.Should().Be(0);

        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task a_worker_token_is_refused_at_the_hive_hub()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        await using var wrongRole = new SwarmHubClient(
            SwarmRole.Hive,
            ConnectionTo(queen.BaseUrl, queen.CreateWorkerToken(), TimeSpan.FromSeconds(1)));

        //Act
        var connected = await wrongRole.StartAsync(cancellationToken);

        //Assert
        connected.Should().BeFalse();
        queen.HiveCount.Should().Be(0);

        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task something_with_no_token_at_all_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        await using var noToken = new SwarmHubClient(
            SwarmRole.Worker,
            ConnectionTo(queen.BaseUrl, "not-a-real-token", TimeSpan.FromSeconds(1)));

        //Act
        var connected = await noToken.StartAsync(cancellationToken);

        //Assert
        connected.Should().BeFalse();
        queen.WorkerCount.Should().Be(0);

        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task the_coordinator_reports_arrivals_and_departures()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        var arrived = new TaskCompletionSource<SwarmConnectionEventArgs>();
        var left = new TaskCompletionSource<SwarmConnectionEventArgs>();
        queen.Connected += (_, e) => arrived.TrySetResult(e);
        queen.Disconnected += (_, e) => left.TrySetResult(e);

        var worker = new SwarmHubClient(
            SwarmRole.Worker,
            ConnectionTo(queen.BaseUrl, queen.CreateWorkerToken(), TimeSpan.FromSeconds(10)));

        //Act
        await worker.StartAsync(cancellationToken);
        var arrival = await arrived.Task.WaitAsync(WaitLimit, cancellationToken);

        await worker.StopAsync(cancellationToken);
        await worker.DisposeAsync();
        var departure = await left.Task.WaitAsync(WaitLimit, cancellationToken);

        //Assert
        arrival.Role.Should().Be(SwarmRole.Worker);
        arrival.WorkerCount.Should().Be(1);
        departure.Role.Should().Be(SwarmRole.Worker);
        departure.WorkerCount.Should().Be(0);

        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task the_coordinator_refuses_to_send_one_of_its_own_message_kinds_by_hand()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);

        //Act
        var act = async () => await queen.SendToWorkersAsync(
            SwarmMessage.Create(SwarmMessageKinds.TerminateWorker), cancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();

        await queen.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task sending_after_the_coordinator_has_stopped_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var queen = await StartQueenAsync(cancellationToken);
        await queen.StopAsync(cancellationToken);

        //Act
        var act = async () => await queen.SendToHivesAsync(
            SwarmMessage.Create("app.anything"), cancellationToken);

        //Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task two_coordinators_at_once_each_get_their_own_port()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await using var first = await StartQueenAsync(cancellationToken);
        await using var second = await StartQueenAsync(cancellationToken);

        //Assert
        first.BaseUrl.Should().NotBe(second.BaseUrl);
        first.WorkerHubUrl.Should().EndWith(SwarmHubContract.WorkerHubPath);
        second.HiveHubUrl.Should().EndWith(SwarmHubContract.HiveHubPath);

        await first.StopAsync(cancellationToken);
        await second.StopAsync(cancellationToken);
    }
}
