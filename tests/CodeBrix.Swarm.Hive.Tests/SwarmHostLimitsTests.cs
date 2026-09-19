using System;
using CodeBrix.Swarm.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests;

public class SwarmHostLimitsTests
{
    [Fact]
    public void the_defaults_are_the_ceilings()
    {
        //Arrange
        var limits = new SwarmHostLimits();

        //Assert
        limits.MaxRamPercent.Should().Be(SwarmHostLimits.RamPercentCeiling);
        limits.MaxCpuPercent.Should().Be(SwarmHostLimits.CpuPercentCeiling);
        limits.MaxRamPercent.Should().Be(90d);
        limits.MaxCpuPercent.Should().Be(90d);
    }

    [Fact]
    public void the_default_floor_is_a_gibibyte_and_the_default_window_is_half_a_minute()
    {
        //Arrange
        var limits = new SwarmHostLimits();

        //Assert
        limits.FreeRamFloorBytes.Should().Be(1024L * 1024L * 1024L);
        limits.CpuAveragingWindow.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void there_is_no_limit_on_the_number_of_workers_by_default()
        => new SwarmHostLimits().MaxWorkers.Should().BeNull();

    [Fact]
    public void Validate_accepts_the_defaults()
    {
        //Arrange
        var validate = () => new SwarmHostLimits().Validate();

        //Act, Assert
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(50d)]
    [InlineData(89.9d)]
    [InlineData(90d)]
    public void Validate_accepts_any_share_of_memory_up_to_the_ceiling(double percent)
    {
        //Arrange
        var validate = () => new SwarmHostLimits { MaxRamPercent = percent }.Validate();

        //Act, Assert
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(90.1d)]
    [InlineData(95d)]
    [InlineData(100d)]
    [InlineData(1000d)]
    public void Validate_refuses_a_share_of_memory_above_the_ceiling(double percent)
    {
        //Arrange - the default is the ceiling: a Hive may be asked to leave more of the host alone,
        //never less. A value above it is refused rather than quietly brought back down, so that nobody
        //believes they were given something they were not.
        var validate = () => new SwarmHostLimits { MaxRamPercent = percent }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    public void Validate_refuses_a_share_of_memory_that_is_not_a_share(double percent)
    {
        //Arrange
        var validate = () => new SwarmHostLimits { MaxRamPercent = percent }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(90.1d)]
    [InlineData(100d)]
    public void Validate_refuses_a_share_of_the_processor_above_the_ceiling(double percent)
    {
        //Arrange
        var validate = () => new SwarmHostLimits { MaxCpuPercent = percent }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void Validate_refuses_a_share_of_the_processor_that_is_not_a_share(double percent)
    {
        //Arrange
        var validate = () => new SwarmHostLimits { MaxCpuPercent = percent }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_accepts_a_floor_of_nothing()
    {
        //Arrange - an application that wants the percentage to be the only word on the subject.
        var validate = () => new SwarmHostLimits { FreeRamFloorBytes = 0L }.Validate();

        //Act, Assert
        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_refuses_a_floor_below_nothing()
    {
        //Arrange
        var validate = () => new SwarmHostLimits { FreeRamFloorBytes = -1L }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_an_averaging_window_of_no_length()
    {
        //Arrange
        var validate = () => new SwarmHostLimits { CpuAveragingWindow = TimeSpan.Zero }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Validate_refuses_a_limit_of_fewer_than_one_worker(int workers)
    {
        //Arrange - leaving it unset is how an application says "as many as the host allows"; zero would
        //mean a Hive that could never start anything.
        var validate = () => new SwarmHostLimits { MaxWorkers = workers }.Validate();

        //Act, Assert
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_accepts_a_limit_of_one_worker()
    {
        //Arrange
        var validate = () => new SwarmHostLimits { MaxWorkers = 1 }.Validate();

        //Act, Assert
        validate.Should().NotThrow();
    }

    [Fact]
    public void Copy_carries_every_value_across()
    {
        //Arrange
        var limits = new SwarmHostLimits
        {
            MaxRamPercent = 55d,
            MaxCpuPercent = 65d,
            FreeRamFloorBytes = 123456789L,
            CpuAveragingWindow = TimeSpan.FromSeconds(17),
            MaxWorkers = 9
        };

        //Act
        var copy = limits.Copy();

        //Assert
        copy.Should().NotBeSameAs(limits);
        copy.MaxRamPercent.Should().Be(55d);
        copy.MaxCpuPercent.Should().Be(65d);
        copy.FreeRamFloorBytes.Should().Be(123456789L);
        copy.CpuAveragingWindow.Should().Be(TimeSpan.FromSeconds(17));
        copy.MaxWorkers.Should().Be(9);
    }

    [Fact]
    public void Copy_leaves_the_original_alone_when_the_copy_is_changed()
    {
        //Arrange
        var limits = new SwarmHostLimits { MaxRamPercent = 80d };

        //Act
        var copy = limits.Copy();
        copy.MaxRamPercent = 10d;

        //Assert
        limits.MaxRamPercent.Should().Be(80d);
    }
}
