using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Queen.Authentication;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Queen.Tests;

public class SwarmQueenOptionsTests
{
    private const string Secret = "a-master-secret-long-enough-for-a-swarm";

    [Fact]
    public void the_default_address_asks_for_any_free_loopback_port()
        => new SwarmQueenOptions().Url.Should().Be(SwarmQueenOptions.AnyFreeLoopbackPortUrl);

    [Fact]
    public void Validate_accepts_an_address_and_a_long_enough_secret()
    {
        //Arrange
        var options = new SwarmQueenOptions { Url = "http://0.0.0.0:5000", MasterSecret = Secret };

        //Act
        var act = options.Validate;

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_accepts_an_https_address()
    {
        //Arrange
        var options = new SwarmQueenOptions { Url = "https://swarm.example:5001", MasterSecret = Secret };

        //Act
        var act = options.Validate;

        //Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("5000")]
    [InlineData("tcp://127.0.0.1:5000")]
    public void Validate_refuses_an_address_that_is_not_an_absolute_web_address(string url)
    {
        //Arrange
        var options = new SwarmQueenOptions { Url = url, MasterSecret = Secret };

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Validate_refuses_a_missing_or_short_secret(string secret)
    {
        //Arrange
        var options = new SwarmQueenOptions { Url = "http://127.0.0.1:0", MasterSecret = secret };

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmTokenException>();
    }
}
