using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

public class LinuxProcStatTests
{
    //The first lines of the file as a Debian host really writes it, per-processor lines included.
    private const string RealFile =
        "cpu  91234 560 18901 3023456 1310 0 452 0 0 0\n"
        + "cpu0 45617 280 9450 1511728 655 0 226 0 0 0\n"
        + "cpu1 45617 280 9451 1511728 655 0 226 0 0 0\n"
        + "intr 123456789 0 0 0\n"
        + "ctxt 987654321\n";

    [Fact]
    public void TryParseTotals_reads_the_whole_host_line_and_not_a_single_processor()
    {
        //Act
        var read = LinuxProcStat.TryParseTotals(RealFile, out var busy, out var total);

        //Assert
        read.Should().BeTrue();

        //user + nice + system + irq + softirq + steal
        busy.Should().Be(91234UL + 560UL + 18901UL + 0UL + 452UL + 0UL);

        //...plus idle and iowait.
        total.Should().Be(busy + 3023456UL + 1310UL);
    }

    [Fact]
    public void TryParseTotals_leaves_the_two_guest_figures_out()
    {
        //Arrange - guest time is already counted inside user and nice, so adding it would count it
        //twice. Both figures are large here and must not show up in either total.
        var text = "cpu  10 0 0 90 0 0 0 0 5000 5000\n";

        //Act
        LinuxProcStat.TryParseTotals(text, out var busy, out var total);

        //Assert
        busy.Should().Be(10UL);
        total.Should().Be(100UL);
    }

    [Fact]
    public void TryParseTotals_reads_an_older_kernel_that_reports_only_the_first_four_figures()
    {
        //Act
        var read = LinuxProcStat.TryParseTotals("cpu  10 5 5 80\n", out var busy, out var total);

        //Assert
        read.Should().BeTrue();
        busy.Should().Be(20UL);
        total.Should().Be(100UL);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cpu0 1 2 3 4 5\n")]
    [InlineData("cpu  1 2\n")]
    [InlineData("cpu  one two three four\n")]
    [InlineData("intr 1 2 3 4 5\n")]
    public void TryParseTotals_refuses_anything_it_cannot_read(string text)
        => LinuxProcStat.TryParseTotals(text, out _, out _).Should().BeFalse();

    [Fact]
    public void BusyPercent_is_the_busy_share_of_the_interval()
        //Arrange, Act, Assert - a quarter of the interval was busy.
        => LinuxProcStat.BusyPercent(100UL, 400UL, 125UL, 500UL).Should().Be(25d);

    [Fact]
    public void BusyPercent_is_nothing_when_no_time_has_passed()
        => LinuxProcStat.BusyPercent(100UL, 400UL, 100UL, 400UL).Should().Be(0d);

    [Fact]
    public void BusyPercent_is_nothing_when_the_counters_started_again()
    {
        //Arrange - a counter that reads lower than it did means the host was restarted, and the
        //interval cannot be measured from these two readings.
        //Act
        var percent = LinuxProcStat.BusyPercent(1000UL, 4000UL, 5UL, 20UL);

        //Assert
        percent.Should().Be(0d);
    }

    [Fact]
    public void BusyPercent_is_a_hundred_when_the_whole_interval_was_busy()
        => LinuxProcStat.BusyPercent(0UL, 0UL, 100UL, 100UL).Should().Be(100d);

    [Fact]
    public void BusyPercent_never_goes_above_a_hundred()
        //Arrange, Act, Assert - a busy figure larger than the whole is nonsense, and is clamped
        //rather than passed on to a comparison against a percentage limit.
        => LinuxProcStat.BusyPercent(0UL, 0UL, 200UL, 100UL).Should().Be(100d);
}
