using System;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Diagnostics;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests;

public class SystemSwarmClockTests
{
    [Fact]
    public void UtcNow_is_in_utc()
        => SystemSwarmClock.Instance.UtcNow.Kind.Should().Be(DateTimeKind.Utc);

    [Fact]
    public async Task DelayAsync_returns_at_once_for_a_span_of_nothing()
    {
        //Arrange
        var before = DateTime.UtcNow;

        //Act
        await SystemSwarmClock.Instance.DelayAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);

        //Assert
        (DateTime.UtcNow - before).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DelayAsync_waits()
    {
        //Arrange
        var before = DateTime.UtcNow;

        //Act
        await SystemSwarmClock.Instance.DelayAsync(
            TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken);

        //Assert
        (DateTime.UtcNow - before).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(15));
    }
}
