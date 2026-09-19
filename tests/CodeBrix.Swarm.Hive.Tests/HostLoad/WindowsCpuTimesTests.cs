using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

/// <summary>
/// The arithmetic behind a Windows processor reading. Windows cannot be run here, so this is the part of
/// that probe that CAN be checked: what the three counters mean and how they combine.
/// </summary>
public class WindowsCpuTimesTests
{
    [Fact]
    public void BusyPercent_treats_the_kernel_figure_as_already_including_idle()
    {
        //Arrange - an interval of 100 units: 80 in the kernel of which 75 were idle, and 20 in user
        //code. The interval is 80 + 20 and the busy part of it is 100 - 75.
        //Act
        var percent = WindowsCpuTimes.BusyPercent(0UL, 0UL, 0UL, 75UL, 80UL, 20UL);

        //Assert
        percent.Should().Be(25d);
    }

    [Fact]
    public void BusyPercent_reports_a_completely_idle_host_as_nothing()
    {
        //Arrange - every unit of the interval was idle, and idle time is reported inside the kernel
        //figure, so kernel and idle move together and user does not move at all.
        //Act
        var percent = WindowsCpuTimes.BusyPercent(1000UL, 2000UL, 500UL, 1100UL, 2100UL, 500UL);

        //Assert
        percent.Should().Be(0d);
    }

    [Fact]
    public void BusyPercent_reports_a_host_with_nothing_idle_as_a_hundred()
        => WindowsCpuTimes.BusyPercent(500UL, 1000UL, 200UL, 500UL, 1060UL, 240UL).Should().Be(100d);

    [Fact]
    public void BusyPercent_is_nothing_when_no_time_has_passed()
        => WindowsCpuTimes.BusyPercent(10UL, 20UL, 30UL, 10UL, 20UL, 30UL).Should().Be(0d);

    [Fact]
    public void BusyPercent_is_nothing_when_the_counters_started_again()
        => WindowsCpuTimes.BusyPercent(1000UL, 2000UL, 500UL, 1UL, 2UL, 1UL).Should().Be(0d);

    [Fact]
    public void BusyPercent_copes_with_more_idle_time_reported_than_the_interval_holds()
    {
        //Arrange - the three counters are read one after another rather than in one instant, so they
        //can disagree slightly. It must read as nothing rather than as a negative share.
        //Act
        var percent = WindowsCpuTimes.BusyPercent(0UL, 0UL, 0UL, 150UL, 100UL, 20UL);

        //Assert
        percent.Should().Be(0d);
    }
}
