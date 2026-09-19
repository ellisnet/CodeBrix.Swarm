using System;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Worker.Tests;

public class SwarmWorkerOptionsTests
{
    private static SwarmWorkerOptions Complete() => new()
    {
        WorkAsync = (_, _) => Task.CompletedTask
    };

    [Fact]
    public void the_window_for_reaching_the_coordinator_is_a_minute_by_default()
        => new SwarmWorkerOptions().QueenUnreachableWindow.Should().Be(TimeSpan.FromSeconds(60));

    [Fact]
    public void Validate_accepts_options_with_work_to_do()
    {
        //Arrange
        var act = Complete().Validate;

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_refuses_options_with_no_work_to_do()
    {
        //Arrange
        var act = new SwarmWorkerOptions().Validate;

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
        options.FirstRetryDelay = TimeSpan.FromSeconds(4);
        options.MaximumRetryDelay = TimeSpan.FromSeconds(1);

        //Act
        var act = options.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }
}
