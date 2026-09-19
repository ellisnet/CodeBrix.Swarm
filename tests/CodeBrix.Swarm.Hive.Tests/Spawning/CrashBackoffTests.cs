using System;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Hive.Spawning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.Spawning;

public class CrashBackoffTests
{
    private static readonly DateTime Noon = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CrashBackoff Backoff() => new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1));

    [Fact]
    public void Current_is_nothing_before_anything_has_failed()
        => Backoff().Current.Should().Be(TimeSpan.Zero);

    [Fact]
    public void RecordExit_starts_the_wait_at_five_seconds_after_the_first_immediate_failure()
    {
        //Arrange
        var backoff = Backoff();

        //Act
        var counted = backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);

        //Assert
        counted.Should().BeTrue();
        backoff.Current.Should().Be(TimeSpan.FromSeconds(5));
        backoff.RemainingAt(Noon).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RecordExit_doubles_the_wait_for_each_further_immediate_failure()
    {
        //Arrange
        var backoff = Backoff();
        var schedule = new TimeSpan[4];

        //Act
        for (var attempt = 0; attempt < schedule.Length; attempt++)
        {
            backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);
            schedule[attempt] = backoff.Current;
        }

        //Assert
        schedule.Should().ContainInOrder(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void RecordExit_never_lets_the_wait_pass_the_ceiling()
    {
        //Arrange
        var backoff = Backoff();

        //Act - far more immediate failures than it takes to reach five minutes.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);
        }

        //Assert
        backoff.Current.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RecordExit_never_waits_after_a_worker_that_ended_normally()
    {
        //Arrange - a Worker that finished its work in a moment is not a failure, however quick it was.
        var backoff = Backoff();

        //Act
        var counted = backoff.RecordExit(Noon, TimeSpan.FromMilliseconds(50), SwarmExitCodes.Success);

        //Assert
        counted.Should().BeFalse();
        backoff.Current.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RecordExit_does_not_wait_after_a_failure_that_came_later_on()
    {
        //Arrange - a Worker that ran for half a minute and then failed did some work first, and the next
        //one may well do more.
        var backoff = Backoff();

        //Act
        var counted = backoff.RecordExit(Noon, TimeSpan.FromSeconds(30), SwarmExitCodes.WorkFailed);

        //Assert
        counted.Should().BeFalse();
        backoff.Current.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RecordExit_clears_everything_once_a_worker_has_survived_long_enough()
    {
        //Arrange - three immediate failures, and then one Worker that lasted two minutes.
        var backoff = Backoff();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);
        }

        backoff.Current.Should().Be(TimeSpan.FromSeconds(20));

        //Act
        backoff.RecordExit(Noon, TimeSpan.FromMinutes(2), SwarmExitCodes.WorkFailed);

        //Assert - whatever was wrong has evidently stopped being wrong.
        backoff.Current.Should().Be(TimeSpan.Zero);
        backoff.RemainingAt(Noon).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RecordExit_starts_from_the_beginning_again_after_a_reset()
    {
        //Arrange
        var backoff = Backoff();
        backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);
        backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);
        backoff.RecordExit(Noon, TimeSpan.FromMinutes(5), SwarmExitCodes.Success);

        //Act
        backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);

        //Assert
        backoff.Current.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RecordFailedStart_waits_as_though_the_worker_had_failed_at_once()
    {
        //Arrange - a program that is not there will not be there next time either.
        var backoff = Backoff();

        //Act
        backoff.RecordFailedStart(Noon);

        //Assert
        backoff.Current.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RemainingAt_counts_down_and_reaches_nothing()
    {
        //Arrange
        var backoff = Backoff();
        backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);

        //Act, Assert
        backoff.RemainingAt(Noon.AddSeconds(2)).Should().Be(TimeSpan.FromSeconds(3));
        backoff.RemainingAt(Noon.AddSeconds(5)).Should().Be(TimeSpan.Zero);
        backoff.RemainingAt(Noon.AddMinutes(1)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Reset_forgets_a_wait_that_was_still_running()
    {
        //Arrange
        var backoff = Backoff();
        backoff.RecordExit(Noon, TimeSpan.FromSeconds(1), SwarmExitCodes.WorkFailed);

        //Act
        backoff.Reset();

        //Assert
        backoff.RemainingAt(Noon).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void a_ceiling_below_the_first_wait_is_raised_to_it()
    {
        //Arrange - an application that asks for a longer first wait than ceiling gets the longer one
        //rather than a schedule that makes no sense.
        var backoff = new CrashBackoff(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(1));

        //Act
        backoff.RecordExit(Noon, TimeSpan.Zero, SwarmExitCodes.WorkFailed);

        //Assert
        backoff.Current.Should().Be(TimeSpan.FromSeconds(30));
    }
}
