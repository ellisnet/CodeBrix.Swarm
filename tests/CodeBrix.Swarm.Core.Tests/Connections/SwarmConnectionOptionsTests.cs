using System;
using CodeBrix.Swarm.Core.Connections;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests.Connections;

public class SwarmConnectionOptionsTests
{
    private static SwarmConnectionOptions Complete() => new()
    {
        QueenUrl = "http://127.0.0.1:5000",
        Token = "an-opaque-token"
    };

    [Fact]
    public void the_window_for_reaching_the_coordinator_is_a_minute_by_default()
        => new SwarmConnectionOptions().QueenUnreachableWindow.Should().Be(TimeSpan.FromSeconds(60));

    [Fact]
    public void Validate_accepts_complete_options()
    {
        //Arrange
        var act = Complete().Validate;

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_refuses_a_missing_address()
    {
        //Arrange
        var options = Complete();
        options.QueenUrl = null;

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_an_address_that_is_not_absolute()
    {
        //Arrange
        var options = Complete();
        options.QueenUrl = "somewhere";

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_accepts_an_https_address()
    {
        //Arrange
        var options = Complete();
        options.QueenUrl = "https://swarm.example:5001";

        //Act
        var act = options.Validate;

        //Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("/swarm/hive")]
    [InlineData("/home/somebody/queen")]
    [InlineData("file:///swarm/hive")]
    [InlineData("ws://127.0.0.1:5000")]
    [InlineData("ftp://127.0.0.1:5000")]
    [InlineData("C:\\swarm\\hive")]
    public void Validate_refuses_an_address_that_is_absolute_but_not_http_or_https(string queenUrl)
    {
        //Arrange - a bare path is an ABSOLUTE FILE ADDRESS on Linux, so "absolute" alone would let one
        //through and the client would then spend its whole retry window failing to connect to it.
        var options = Complete();
        options.QueenUrl = queenUrl;

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_missing_token()
    {
        //Arrange
        var options = Complete();
        options.Token = "   ";

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_negative_window()
    {
        //Arrange
        var options = Complete();
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(-1);

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_longest_delay_shorter_than_the_first_one()
    {
        //Arrange
        var options = Complete();
        options.FirstRetryDelay = TimeSpan.FromSeconds(5);
        options.MaximumRetryDelay = TimeSpan.FromSeconds(1);

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }
}
