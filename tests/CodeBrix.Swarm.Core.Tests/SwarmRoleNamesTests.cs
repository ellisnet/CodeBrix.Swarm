using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests;

public class SwarmRoleNamesTests
{
    [Fact]
    public void the_two_roles_have_different_names()
        => SwarmRoleNames.Hive.Should().NotBe(SwarmRoleNames.Worker);

    [Fact]
    public void For_gives_each_role_its_wire_name()
    {
        //Assert
        SwarmRoleNames.For(SwarmRole.Hive).Should().Be(SwarmRoleNames.Hive);
        SwarmRoleNames.For(SwarmRole.Worker).Should().Be(SwarmRoleNames.Worker);
    }

    [Fact]
    public void For_refuses_a_role_the_swarm_does_not_know()
    {
        //Act
        var act = () => SwarmRoleNames.For((SwarmRole)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryParse_reads_a_wire_name_back()
    {
        //Act
        var parsed = SwarmRoleNames.TryParse(SwarmRoleNames.Worker, out var role);

        //Assert
        parsed.Should().BeTrue();
        role.Should().Be(SwarmRole.Worker);
    }

    [Theory]
    [InlineData("Hive")]
    [InlineData("WORKER")]
    [InlineData("queen")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_refuses_anything_that_is_not_an_exact_wire_name(string name)
        => SwarmRoleNames.TryParse(name, out _).Should().BeFalse();
}
