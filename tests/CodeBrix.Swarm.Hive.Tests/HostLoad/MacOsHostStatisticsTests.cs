using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

/// <summary>
/// The arithmetic behind a macOS reading. macOS cannot be run here, so this is the part of that probe
/// that CAN be checked: which of the kernel's page counts mean "in use", and how the tick counters
/// combine.
/// </summary>
public class MacOsHostStatisticsTests
{
    private const long PageSize = 16384L;

    private const long SixteenGibibytes = 16L * 1024L * 1024L * 1024L;

    [Fact]
    public void AvailableBytes_counts_process_memory_wired_memory_and_the_compressor_as_used()
    {
        //Arrange - a hundred thousand pages of each of the three kinds that count, and none purgeable.
        const uint pagesOfEach = 100_000U;

        //Act
        var available = MacOsHostStatistics.AvailableBytes(
            SixteenGibibytes, PageSize, pagesOfEach, 0U, pagesOfEach, pagesOfEach);

        //Assert
        available.Should().Be(SixteenGibibytes - (3L * pagesOfEach * PageSize));
    }

    [Fact]
    public void AvailableBytes_takes_the_purgeable_pages_back_off_again()
    {
        //Arrange - purgeable pages are counted inside the process pages, and the kernel may throw them
        //away whenever it likes, so they are available.
        //Act
        var withNonePurgeable = MacOsHostStatistics.AvailableBytes(
            SixteenGibibytes, PageSize, 200_000U, 0U, 0U, 0U);

        var withHalfPurgeable = MacOsHostStatistics.AvailableBytes(
            SixteenGibibytes, PageSize, 200_000U, 100_000U, 0U, 0U);

        //Assert
        (withHalfPurgeable - withNonePurgeable).Should().Be(100_000L * PageSize);
    }

    [Fact]
    public void AvailableBytes_does_not_count_file_backed_pages_as_used()
    {
        //Arrange - file-backed pages are the same thing as a Linux page cache: a host fills itself with
        //them and hands them back the instant something asks. The count is not even a parameter here,
        //which is the point: a host with nothing but caches in it reads as entirely available.
        //Act
        var available = MacOsHostStatistics.AvailableBytes(SixteenGibibytes, PageSize, 0U, 0U, 0U, 0U);

        //Assert
        available.Should().Be(SixteenGibibytes);
    }

    [Fact]
    public void AvailableBytes_never_reports_more_used_than_the_host_has()
    {
        //Arrange - far more pages in use than the host could hold.
        //Act
        var available = MacOsHostStatistics.AvailableBytes(
            SixteenGibibytes, PageSize, uint.MaxValue, 0U, uint.MaxValue, uint.MaxValue);

        //Assert
        available.Should().Be(0L);
    }

    [Fact]
    public void AvailableBytes_copes_with_more_purgeable_pages_reported_than_process_pages()
    {
        //Arrange - the counts are read in one call but are maintained separately, so they can disagree.
        //Act
        var available = MacOsHostStatistics.AvailableBytes(
            SixteenGibibytes, PageSize, 10U, 1_000U, 0U, 0U);

        //Assert
        available.Should().Be(SixteenGibibytes);
    }

    [Theory]
    [InlineData(0L, PageSize)]
    [InlineData(SixteenGibibytes, 0L)]
    public void AvailableBytes_is_nothing_when_the_host_facts_are_missing(long total, long pageSize)
        => MacOsHostStatistics
            .AvailableBytes(total, pageSize, 1U, 0U, 1U, 1U)
            .Should()
            .Be(0L);

    [Fact]
    public void BusyPercent_counts_user_system_and_nice_as_busy_and_idle_as_not()
    {
        //Arrange
        var previous = new MacOsCpuTicks(0U, 0U, 0U, 0U);
        var current = new MacOsCpuTicks(10U, 5U, 75U, 10U);

        //Act
        var percent = MacOsHostStatistics.BusyPercent(previous, current);

        //Assert - 25 of the 100 ticks were spent doing something.
        percent.Should().Be(25d);
    }

    [Fact]
    public void BusyPercent_counts_the_wrap_when_a_counter_fills_up()
    {
        //Arrange - the tick counters are 32 bits wide and wrap round; a reading that straddles one must
        //be right rather than nonsense.
        var previous = new MacOsCpuTicks(uint.MaxValue - 9U, 0U, uint.MaxValue - 69U, 0U);
        var current = new MacOsCpuTicks(10U, 0U, 10U, 0U);

        //Act
        var percent = MacOsHostStatistics.BusyPercent(previous, current);

        //Assert - 20 busy ticks and 100 altogether.
        percent.Should().Be(20d);
    }

    [Fact]
    public void BusyPercent_is_nothing_when_no_ticks_have_passed()
    {
        //Arrange
        var ticks = new MacOsCpuTicks(100U, 200U, 300U, 400U);

        //Act, Assert
        MacOsHostStatistics.BusyPercent(ticks, ticks).Should().Be(0d);
    }

    [Fact]
    public void BusyPercent_is_a_hundred_when_nothing_was_idle()
    {
        //Arrange
        var previous = new MacOsCpuTicks(0U, 0U, 50U, 0U);
        var current = new MacOsCpuTicks(40U, 10U, 50U, 0U);

        //Act, Assert
        MacOsHostStatistics.BusyPercent(previous, current).Should().Be(100d);
    }
}
