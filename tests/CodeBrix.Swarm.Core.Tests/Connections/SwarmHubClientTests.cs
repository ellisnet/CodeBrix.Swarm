using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.Core.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests.Connections;

public class SwarmHubClientTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static SwarmConnectionOptions Options(TimeSpan window) => new()
    {
        QueenUrl = "http://127.0.0.1:5000",
        Token = "an-opaque-token",
        QueenUnreachableWindow = window,
        FirstRetryDelay = TimeSpan.FromMilliseconds(250),
        MaximumRetryDelay = TimeSpan.FromSeconds(3)
    };

    [Fact]
    public async Task StartAsync_connects_to_the_hub_for_its_role_with_its_token()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        //Act
        var connected = await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        connected.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
        factory.Created.Should().HaveCount(1);
        factory.Latest.HubUrl.Should().Be("http://127.0.0.1:5000" + SwarmHubContract.WorkerHubPath);
        factory.Latest.AccessToken.Should().Be("an-opaque-token");
        factory.Latest.RegisteredMethodName.Should().Be(SwarmHubContract.ReceiveMessageMethodName);
        factory.Latest.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_uses_the_hive_hub_for_a_hive()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var client = new SwarmHubClient(
            SwarmRole.Hive, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        //Act
        await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        factory.Latest.HubUrl.Should().EndWith(SwarmHubContract.HiveHubPath);
    }

    [Fact]
    public async Task StartAsync_keeps_trying_and_succeeds_on_a_later_attempt()
    {
        //Arrange - the first three attempts are refused.
        var factory = new FakeHubConnectionFactory(attempt => attempt < 3);
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        //Act
        var connected = await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        connected.Should().BeTrue();
        factory.Created.Should().HaveCount(4);
    }

    [Fact]
    public async Task StartAsync_gives_up_when_the_window_runs_out_and_says_the_coordinator_is_out_of_reach()
    {
        //Arrange
        var clock = new FakeSwarmClock();
        var factory = new FakeHubConnectionFactory(_ => true);
        var outOfReach = 0;

        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, clock);
        client.QueenUnreachable += (_, _) => Interlocked.Increment(ref outOfReach);

        //Act
        var connected = await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        connected.Should().BeFalse();
        client.IsConnected.Should().BeFalse();
        outOfReach.Should().Be(1);
        factory.Created.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task StartAsync_waits_no_longer_than_the_window_altogether()
    {
        //Arrange
        var startedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeSwarmClock(startedAt);
        var factory = new FakeHubConnectionFactory(_ => true);

        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, clock);

        //Act
        await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        (clock.UtcNow - startedAt).Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task StartAsync_starts_quickly_and_settles_into_the_longest_delay()
    {
        //Arrange
        var clock = new FakeSwarmClock();
        var factory = new FakeHubConnectionFactory(_ => true);

        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, clock);

        //Act
        await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        var delays = clock.Delays;
        delays[0].Should().Be(TimeSpan.FromMilliseconds(250));
        delays[1].Should().Be(TimeSpan.FromMilliseconds(500));
        delays[2].Should().Be(TimeSpan.FromSeconds(1));
        delays[3].Should().Be(TimeSpan.FromSeconds(2));
        delays[4].Should().Be(TimeSpan.FromSeconds(3));
        delays[5].Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task a_window_of_nothing_means_one_attempt_and_no_waiting()
    {
        //Arrange
        var clock = new FakeSwarmClock();
        var factory = new FakeHubConnectionFactory(_ => true);

        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.Zero), factory, clock);

        //Act
        var connected = await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        connected.Should().BeFalse();
        factory.Created.Should().HaveCount(1);
        clock.Delays.Should().BeEmpty();
    }

    [Fact]
    public async Task a_message_reaches_the_handler_registered_for_its_kind()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        SwarmMessage received = null;
        client.Messages.Register("app.one", (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        });

        await client.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await factory.Latest.DeliverAsync(SwarmMessage.Create("app.one"));

        //Assert
        received.Should().NotBeNull();
    }

    [Fact]
    public async Task a_message_nobody_registered_for_is_ignored_quietly()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        var errors = 0;
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());
        client.Error += (_, _) => Interlocked.Increment(ref errors);

        await client.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await factory.Latest.DeliverAsync(SwarmMessage.Create("app.nobody-wants-this"));

        //Assert
        errors.Should().Be(0);
    }

    [Fact]
    public async Task a_handler_that_throws_is_reported_and_does_not_close_the_connection()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        var errors = 0;
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());
        client.Error += (_, _) => Interlocked.Increment(ref errors);
        client.Messages.Register("app.one", (_, _) => throw new InvalidOperationException("no"));

        await client.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await factory.Latest.DeliverAsync(SwarmMessage.Create("app.one"));

        //Assert
        errors.Should().Be(1);
        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task OnTerminate_listens_for_the_terminate_kind_of_its_own_role()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var worker = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        var told = false;
        worker.OnTerminate(_ =>
        {
            told = true;
            return Task.CompletedTask;
        });

        await worker.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await factory.Latest.DeliverAsync(SwarmMessage.Create(SwarmMessageKinds.TerminateWorker));

        //Assert
        told.Should().BeTrue();
        worker.Messages.IsRegistered(SwarmMessageKinds.TerminateAllWorkers).Should().BeFalse();
    }

    [Fact]
    public async Task OnTerminate_on_a_hive_listens_for_the_kind_that_ends_all_its_workers()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var hive = new SwarmHubClient(
            SwarmRole.Hive, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        var told = false;
        hive.OnTerminate(_ =>
        {
            told = true;
            return Task.CompletedTask;
        });

        await hive.StartAsync(TestContext.Current.CancellationToken);

        //Act
        await factory.Latest.DeliverAsync(SwarmMessage.Create(SwarmMessageKinds.TerminateAllWorkers));

        //Assert
        told.Should().BeTrue();
    }

    [Fact]
    public async Task a_dropped_connection_is_opened_again()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        var reconnected = new TaskCompletionSource();
        var connections = 0;
        client.Connected += (_, _) =>
        {
            if (Interlocked.Increment(ref connections) == 2)
            {
                reconnected.TrySetResult();
            }
        };

        await client.StartAsync(TestContext.Current.CancellationToken);
        var first = factory.Latest;

        //Act
        await first.DropAsync(new InvalidOperationException("the coordinator went away"));
        await reconnected.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        //Assert
        factory.Created.Should().HaveCount(2);
        client.IsConnected.Should().BeTrue();
        first.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_closes_the_connection_and_does_not_open_another()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        await client.StartAsync(TestContext.Current.CancellationToken);
        var connection = factory.Latest;

        //Act
        await client.StopAsync(TestContext.Current.CancellationToken);
        await connection.DropAsync(null);

        //Assert
        connection.IsStopped.Should().BeTrue();
        connection.IsDisposed.Should().BeTrue();
        client.IsConnected.Should().BeFalse();
        factory.Created.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartAsync_a_second_time_keeps_the_connection_it_has()
    {
        //Arrange
        var factory = new FakeHubConnectionFactory();
        await using var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)), factory, new FakeSwarmClock());

        //Act
        await client.StartAsync(TestContext.Current.CancellationToken);
        var again = await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        again.Should().BeTrue();
        factory.Created.Should().HaveCount(1);
    }

    [Fact]
    public void the_client_refuses_options_that_do_not_make_sense()
    {
        //Arrange
        var options = new SwarmConnectionOptions { QueenUrl = null, Token = "token" };

        //Act
        var act = () => new SwarmHubClient(
            SwarmRole.Worker, options, new FakeHubConnectionFactory(), new FakeSwarmClock());

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public async Task StartAsync_after_disposal_is_refused()
    {
        //Arrange
        var client = new SwarmHubClient(
            SwarmRole.Worker, Options(TimeSpan.FromSeconds(60)),
            new FakeHubConnectionFactory(), new FakeSwarmClock());
        await client.DisposeAsync();

        //Act
        var act = async () => await client.StartAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
