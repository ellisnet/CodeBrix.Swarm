using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Messaging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests.Messaging;

public class SwarmMessageDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_hands_the_message_to_the_handler_for_its_kind()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();
        SwarmMessage received = null;
        dispatcher.Register("app.one", (message, _) =>
        {
            received = message;
            return Task.CompletedTask;
        });

        //Act
        await dispatcher.DispatchAsync(SwarmMessage.Create("app.one"), TestContext.Current.CancellationToken);

        //Assert
        received.Should().NotBeNull();
        received.Kind.Should().Be("app.one");
    }

    [Fact]
    public async Task DispatchAsync_ignores_a_kind_nobody_registered_for()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();
        var called = false;
        dispatcher.Register("app.one", (_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        });

        //Act
        await dispatcher.DispatchAsync(SwarmMessage.Create("app.two"), TestContext.Current.CancellationToken);

        //Assert
        called.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_calls_every_handler_for_a_kind_in_order()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();
        var order = new List<string>();
        dispatcher.Register("app.one", (_, _) =>
        {
            order.Add("first");
            return Task.CompletedTask;
        });
        dispatcher.Register("app.one", (_, _) =>
        {
            order.Add("second");
            return Task.CompletedTask;
        });

        //Act
        await dispatcher.DispatchAsync(SwarmMessage.Create("app.one"), TestContext.Current.CancellationToken);

        //Assert
        order.Should().Equal("first", "second");
    }

    [Fact]
    public async Task DispatchAsync_calls_the_rest_when_one_handler_throws_and_reports_them_together()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();
        var secondRan = false;
        dispatcher.Register("app.one", (_, _) => throw new InvalidOperationException("no"));
        dispatcher.Register("app.one", (_, _) =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });

        //Act
        var act = async () => await dispatcher.DispatchAsync(
            SwarmMessage.Create("app.one"), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<AggregateException>();
        secondRan.Should().BeTrue();
    }

    [Fact]
    public void kinds_are_matched_exactly_and_are_case_sensitive()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();

        //Act
        dispatcher.Register("app.one", (_, _) => Task.CompletedTask);

        //Assert
        dispatcher.IsRegistered("app.one").Should().BeTrue();
        dispatcher.IsRegistered("App.One").Should().BeFalse();
    }

    [Fact]
    public void Unregister_removes_only_the_handler_it_was_given()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();
        SwarmMessageHandler first = (_, _) => Task.CompletedTask;
        SwarmMessageHandler second = (_, _) => Task.CompletedTask;
        dispatcher.Register("app.one", first);
        dispatcher.Register("app.one", second);

        //Act
        var removed = dispatcher.Unregister("app.one", first);

        //Assert
        removed.Should().BeTrue();
        dispatcher.IsRegistered("app.one").Should().BeTrue();
        dispatcher.Unregister("app.one", second).Should().BeTrue();
        dispatcher.IsRegistered("app.one").Should().BeFalse();
    }

    [Fact]
    public void Unregister_reports_false_for_a_handler_that_was_never_registered()
        => new SwarmMessageDispatcher()
            .Unregister("app.one", (_, _) => Task.CompletedTask)
            .Should().BeFalse();

    [Fact]
    public void Register_refuses_a_missing_kind()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();

        //Act
        var act = () => dispatcher.Register("  ", (_, _) => Task.CompletedTask);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_refuses_a_missing_handler()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();

        //Act
        var act = () => dispatcher.Register("app.one", null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DispatchAsync_refuses_a_missing_message()
    {
        //Arrange
        var dispatcher = new SwarmMessageDispatcher();

        //Act
        var act = async () => await dispatcher.DispatchAsync(null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
