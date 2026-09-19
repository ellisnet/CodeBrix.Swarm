using System;
using System.Collections.Generic;
using CodeBrix.Swarm.Hive.Spawning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.Spawning;

/// <summary>
/// The three environment variables that let whoever looks after a host hold a Hive back. The one thing
/// that matters about all of them: THEY CAN ONLY LOWER.
/// </summary>
public class HostLimitsFromEnvironmentTests
{
    private static Func<string, string> Environment(params (string Name, string Value)[] variables)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variable in variables)
        {
            values[variable.Name] = variable.Value;
        }

        return name => values.TryGetValue(name, out var value) ? value : null;
    }

    [Fact]
    public void Apply_changes_nothing_when_no_variable_is_set()
    {
        //Arrange
        var limits = new SwarmHostLimits();

        //Act
        var applied = HostLimitsFromEnvironment.Apply(limits, Environment());

        //Assert
        applied.MaxRamPercent.Should().Be(SwarmHostLimits.RamPercentCeiling);
        applied.MaxCpuPercent.Should().Be(SwarmHostLimits.CpuPercentCeiling);
        applied.MaxWorkers.Should().BeNull();
    }

    [Fact]
    public void Apply_never_changes_the_limits_it_was_handed()
    {
        //Arrange
        var limits = new SwarmHostLimits();

        //Act
        HostLimitsFromEnvironment.Apply(
            limits,
            Environment((SwarmHostLimits.MaxRamPercentVariable, "40")));

        //Assert - the application's own object is left exactly as it was.
        limits.MaxRamPercent.Should().Be(SwarmHostLimits.RamPercentCeiling);
    }

    [Fact]
    public void Apply_lowers_the_share_of_memory()
    {
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits(),
            Environment((SwarmHostLimits.MaxRamPercentVariable, "55.5")));

        //Assert
        applied.MaxRamPercent.Should().Be(55.5d);
    }

    [Fact]
    public void Apply_refuses_to_raise_the_share_of_memory()
    {
        //Arrange - an application that asked to be gentle with the host.
        var limits = new SwarmHostLimits { MaxRamPercent = 40d };

        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            limits,
            Environment((SwarmHostLimits.MaxRamPercentVariable, "85")));

        //Assert - the host's own environment cannot talk a Hive into taking more than the application
        //considered safe.
        applied.MaxRamPercent.Should().Be(40d);
    }

    [Fact]
    public void Apply_refuses_to_raise_the_share_of_memory_past_the_ceiling()
    {
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits(),
            Environment((SwarmHostLimits.MaxRamPercentVariable, "99")));

        //Assert
        applied.MaxRamPercent.Should().Be(SwarmHostLimits.RamPercentCeiling);
    }

    [Fact]
    public void Apply_lowers_the_share_of_the_processor()
    {
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits(),
            Environment((SwarmHostLimits.MaxCpuPercentVariable, "25")));

        //Assert
        applied.MaxCpuPercent.Should().Be(25d);
    }

    [Fact]
    public void Apply_refuses_to_raise_the_share_of_the_processor()
    {
        //Arrange
        var limits = new SwarmHostLimits { MaxCpuPercent = 30d };

        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            limits,
            Environment((SwarmHostLimits.MaxCpuPercentVariable, "90")));

        //Assert
        applied.MaxCpuPercent.Should().Be(30d);
    }

    [Fact]
    public void Apply_sets_a_limit_on_workers_where_the_application_asked_for_none()
    {
        //Arrange - no limit at all is the default, so any number the environment gives is lower.
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits(),
            Environment((SwarmHostLimits.MaxWorkersVariable, "3")));

        //Assert
        applied.MaxWorkers.Should().Be(3);
    }

    [Fact]
    public void Apply_lowers_a_limit_on_workers_the_application_had_already_set()
    {
        //Arrange
        var limits = new SwarmHostLimits { MaxWorkers = 10 };

        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            limits,
            Environment((SwarmHostLimits.MaxWorkersVariable, "4")));

        //Assert
        applied.MaxWorkers.Should().Be(4);
    }

    [Fact]
    public void Apply_refuses_to_raise_a_limit_on_workers()
    {
        //Arrange
        var limits = new SwarmHostLimits { MaxWorkers = 2 };

        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            limits,
            Environment((SwarmHostLimits.MaxWorkersVariable, "50")));

        //Assert
        applied.MaxWorkers.Should().Be(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("half")]
    [InlineData("-10")]
    [InlineData("0")]
    public void Apply_ignores_a_share_of_memory_it_cannot_read(string value)
    {
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits { MaxRamPercent = 60d },
            Environment((SwarmHostLimits.MaxRamPercentVariable, value)));

        //Assert - a misspelt value holds nothing back, which is the safe direction to be wrong in.
        applied.MaxRamPercent.Should().Be(60d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    public void Apply_ignores_a_limit_on_workers_it_cannot_read(string value)
    {
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits(),
            Environment((SwarmHostLimits.MaxWorkersVariable, value)));

        //Assert
        applied.MaxWorkers.Should().BeNull();
    }

    [Fact]
    public void Apply_lowers_all_three_at_once()
    {
        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            new SwarmHostLimits(),
            Environment(
                (SwarmHostLimits.MaxRamPercentVariable, "50"),
                (SwarmHostLimits.MaxCpuPercentVariable, "60"),
                (SwarmHostLimits.MaxWorkersVariable, "2")));

        //Assert
        applied.MaxRamPercent.Should().Be(50d);
        applied.MaxCpuPercent.Should().Be(60d);
        applied.MaxWorkers.Should().Be(2);
    }

    [Fact]
    public void Apply_carries_the_rest_of_the_limits_through_untouched()
    {
        //Arrange
        var limits = new SwarmHostLimits
        {
            FreeRamFloorBytes = 2L * 1024L * 1024L * 1024L,
            CpuAveragingWindow = TimeSpan.FromSeconds(45)
        };

        //Act
        var applied = HostLimitsFromEnvironment.Apply(
            limits,
            Environment((SwarmHostLimits.MaxRamPercentVariable, "50")));

        //Assert
        applied.FreeRamFloorBytes.Should().Be(2L * 1024L * 1024L * 1024L);
        applied.CpuAveragingWindow.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Apply_ignores_an_environment_that_throws_when_it_is_read()
    {
        //Arrange - reading an environment variable is not supposed to fail, but a Hive that could not
        //start because of it would be worse than one that simply carries on.
        Func<string, string> awkward = _ => throw new InvalidOperationException("no environment here");

        //Act
        var applied = HostLimitsFromEnvironment.Apply(new SwarmHostLimits(), awkward);

        //Assert
        applied.MaxRamPercent.Should().Be(SwarmHostLimits.RamPercentCeiling);
    }
}
