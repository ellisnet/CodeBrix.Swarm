using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests;

public class SwarmHubContractTests
{
    [Fact]
    public void the_two_hubs_are_at_different_paths()
        => SwarmHubContract.HiveHubPath.Should().NotBe(SwarmHubContract.WorkerHubPath);

    [Fact]
    public void HubPathFor_gives_each_role_its_own_path()
    {
        //Assert
        SwarmHubContract.HubPathFor(SwarmRole.Hive).Should().Be(SwarmHubContract.HiveHubPath);
        SwarmHubContract.HubPathFor(SwarmRole.Worker).Should().Be(SwarmHubContract.WorkerHubPath);
    }

    [Fact]
    public void HubPathFor_refuses_a_role_the_swarm_does_not_know()
    {
        //Act
        var act = () => SwarmHubContract.HubPathFor((SwarmRole)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("  http://127.0.0.1:5000/  ")]
    public void HubUrlFor_joins_the_address_and_the_path_without_doubling_the_slash(string baseUrl)
        => SwarmHubContract.HubUrlFor(baseUrl, SwarmRole.Worker)
            .Should().Be("http://127.0.0.1:5000" + SwarmHubContract.WorkerHubPath);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HubUrlFor_refuses_a_missing_address(string baseUrl)
    {
        //Act
        var act = () => SwarmHubContract.HubUrlFor(baseUrl, SwarmRole.Hive);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_client_method_name_is_spelled_once()
        => SwarmHubContract.ReceiveMessageMethodName.Should().NotBeNullOrWhiteSpace();
}
