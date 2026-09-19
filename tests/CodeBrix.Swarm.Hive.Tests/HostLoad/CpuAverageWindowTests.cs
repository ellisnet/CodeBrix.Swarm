using System;
using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

public class CpuAverageWindowTests
{
    private static readonly DateTime Noon = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Average_is_nothing_before_anything_has_been_measured()
        //A Hive that has only just started must not be held back by a figure it has not taken yet.
        => new CpuAverageWindow(TimeSpan.FromSeconds(30)).Average(Noon).Should().Be(0d);

    [Fact]
    public void Average_is_the_mean_of_what_is_inside_the_window()
    {
        //Arrange
        var window = new CpuAverageWindow(TimeSpan.FromSeconds(30));
        window.Add(Noon, 10d);
        window.Add(Noon.AddSeconds(10), 20d);
        window.Add(Noon.AddSeconds(20), 60d);

        //Act
        var average = window.Average(Noon.AddSeconds(20));

        //Assert
        average.Should().Be(30d);
    }

    [Fact]
    public void Average_lets_go_of_readings_older_than_the_window()
    {
        //Arrange - one very busy reading a long time ago, and two quiet ones since.
        var window = new CpuAverageWindow(TimeSpan.FromSeconds(30));
        window.Add(Noon, 100d);
        window.Add(Noon.AddSeconds(60), 10d);
        window.Add(Noon.AddSeconds(70), 20d);

        //Act
        var average = window.Average(Noon.AddSeconds(70));

        //Assert - the old reading is long gone, so a host that was busy a minute ago is not treated as
        //busy now.
        average.Should().Be(15d);
        window.SampleCount.Should().Be(2);
    }

    [Fact]
    public void Average_is_nothing_again_once_every_reading_has_aged_out()
    {
        //Arrange
        var window = new CpuAverageWindow(TimeSpan.FromSeconds(30));
        window.Add(Noon, 100d);

        //Act
        var average = window.Average(Noon.AddMinutes(5));

        //Assert
        average.Should().Be(0d);
        window.SampleCount.Should().Be(0);
    }

    [Fact]
    public void Add_ignores_a_reading_that_is_not_a_number()
    {
        //Arrange
        var window = new CpuAverageWindow(TimeSpan.FromSeconds(30));

        //Act
        window.Add(Noon, double.NaN);

        //Assert
        window.SampleCount.Should().Be(0);
        window.Average(Noon).Should().Be(0d);
    }

    [Fact]
    public void the_window_must_be_longer_than_nothing()
    {
        //Arrange
        var create = () => new CpuAverageWindow(TimeSpan.Zero);

        //Act, Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void one_very_busy_instant_does_not_decide_anything_by_itself()
    {
        //Arrange - the whole reason the figure is an average. Every host reads as completely busy for
        //the instant something wakes up on it, and a Hive that acted on one reading would stop starting
        //Workers on a host that is doing nothing at all.
        var window = new CpuAverageWindow(TimeSpan.FromSeconds(30));

        for (var second = 0; second < 29; second++)
        {
            window.Add(Noon.AddSeconds(second), 0d);
        }

        window.Add(Noon.AddSeconds(29), 100d);

        //Act
        var average = window.Average(Noon.AddSeconds(29));

        //Assert - well under any limit a Hive is allowed to have.
        average.Should().BeLessThan(10d);
    }
}
