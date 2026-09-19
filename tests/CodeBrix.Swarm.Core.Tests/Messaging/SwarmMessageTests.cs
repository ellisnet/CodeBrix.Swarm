using System;
using System.Text.Json;
using CodeBrix.Swarm.Core.Messaging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests.Messaging;

public class SwarmMessageTests
{
    private sealed class WorkOrder
    {
        public string Name { get; set; }

        public int Count { get; set; }
    }

    [Fact]
    public void Create_stamps_an_identifier_and_a_utc_time()
    {
        //Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        //Act
        var message = SwarmMessage.Create("app.something");

        //Assert
        message.MessageId.Should().NotBe(Guid.Empty);
        message.Kind.Should().Be("app.something");
        message.SentUtc.Should().BeOnOrAfter(before);
        message.HasPayload.Should().BeFalse();
    }

    [Fact]
    public void Create_gives_every_message_a_different_identifier()
    {
        //Act
        var first = SwarmMessage.Create("app.something");
        var second = SwarmMessage.Create("app.something");

        //Assert
        first.MessageId.Should().NotBe(second.MessageId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_refuses_a_missing_kind(string kind)
    {
        //Act
        var act = () => SwarmMessage.Create(kind);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_with_a_payload_round_trips_it()
    {
        //Arrange
        var order = new WorkOrder { Name = "second batch", Count = 17 };

        //Act
        var message = SwarmMessage.Create("app.work-order", order);
        var read = message.GetPayload<WorkOrder>();

        //Assert
        message.HasPayload.Should().BeTrue();
        read.Name.Should().Be("second batch");
        read.Count.Should().Be(17);
    }

    [Fact]
    public void Create_with_a_null_payload_makes_a_message_with_no_body()
    {
        //Act
        var message = SwarmMessage.Create<WorkOrder>("app.work-order", null);

        //Assert
        message.HasPayload.Should().BeFalse();
        message.Payload.Should().BeNull();
    }

    [Fact]
    public void GetPayload_returns_the_default_when_there_is_no_body()
        => SwarmMessage.Create("app.something").GetPayload<WorkOrder>().Should().BeNull();

    [Fact]
    public void GetPayload_throws_when_the_body_is_not_that_type()
    {
        //Arrange
        var message = SwarmMessage.Create("app.something");
        message.Payload = "\"just a string\"";

        //Act
        var act = () => message.GetPayload<WorkOrder>();

        //Assert
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void the_envelope_survives_being_serialized_whole()
    {
        //Arrange
        var message = SwarmMessage.Create("app.work-order", new WorkOrder { Name = "batch", Count = 3 });

        //Act
        var json = JsonSerializer.Serialize(message);
        var read = JsonSerializer.Deserialize<SwarmMessage>(json);

        //Assert
        read.MessageId.Should().Be(message.MessageId);
        read.Kind.Should().Be(message.Kind);
        read.Payload.Should().Be(message.Payload);
        read.GetPayload<WorkOrder>().Count.Should().Be(3);
    }
}
