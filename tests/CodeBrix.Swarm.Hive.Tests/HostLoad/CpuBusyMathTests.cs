using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

public class CpuBusyMathTests
{
    [Theory]
    [InlineData(0UL, 100UL, 0d)]
    [InlineData(50UL, 100UL, 50d)]
    [InlineData(100UL, 100UL, 100d)]
    [InlineData(1UL, 8UL, 12.5d)]
    public void Percent_is_the_share_of_the_interval(ulong busy, ulong total, double expected)
        => CpuBusyMath.Percent(busy, total).Should().Be(expected);

    [Fact]
    public void Percent_is_nothing_for_an_interval_of_no_length()
        => CpuBusyMath.Percent(5UL, 0UL).Should().Be(0d);

    [Fact]
    public void Percent_never_goes_above_a_hundred()
        => CpuBusyMath.Percent(500UL, 100UL).Should().Be(100d);

    [Fact]
    public void Difference_is_how_much_the_counter_grew()
        => CpuBusyMath.Difference(100UL, 175UL).Should().Be(75UL);

    [Fact]
    public void Difference_is_nothing_when_the_counter_went_backwards()
        => CpuBusyMath.Difference(100UL, 5UL).Should().Be(0UL);

    [Fact]
    public void WrappingDifference_counts_the_wrap_round()
    {
        //Arrange - a 32-bit counter three short of filling up, read again two after it wrapped.
        const uint previous = uint.MaxValue - 2U;
        const uint current = 2U;

        //Act
        var difference = CpuBusyMath.WrappingDifference(previous, current);

        //Assert - three ticks to the wrap and two after it.
        difference.Should().Be(5UL);
    }

    [Fact]
    public void WrappingDifference_is_the_plain_difference_when_nothing_wrapped()
        => CpuBusyMath.WrappingDifference(10U, 40U).Should().Be(30UL);
}
