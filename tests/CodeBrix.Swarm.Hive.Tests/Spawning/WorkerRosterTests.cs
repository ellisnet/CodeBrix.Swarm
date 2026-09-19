using System;
using CodeBrix.Swarm.Hive.Spawning;
using CodeBrix.Swarm.Hive.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.Spawning;

public class WorkerRosterTests
{
    private static readonly DateTime Noon = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static FakeWorkerProcess Worker(string workerId, int processId, DateTime startedUtc)
        => new(
            new WorkerProcessRequest(workerId, "worker", [], null, "{}", false, null),
            processId,
            startedUtc);

    [Fact]
    public void NextWorkerId_gives_every_worker_a_different_name()
    {
        //Arrange
        var roster = new WorkerRoster("host-one");

        //Act
        var first = roster.NextWorkerId();
        roster.Add(Worker(first, 1, Noon));
        var second = roster.NextWorkerId();
        roster.Add(Worker(second, 2, Noon));
        var third = roster.NextWorkerId();

        //Assert
        first.Should().Be("host-one-1");
        second.Should().Be("host-one-2");
        third.Should().Be("host-one-3");
    }

    [Fact]
    public void StartedCount_counts_every_worker_ever_started_and_Count_only_the_running_ones()
    {
        //Arrange
        var roster = new WorkerRoster("host");
        var first = Worker("host-1", 1, Noon);
        roster.Add(first);
        roster.Add(Worker("host-2", 2, Noon));

        //Act
        first.Exit(0);
        roster.Reap(Noon.AddSeconds(30), TimeSpan.FromSeconds(10));

        //Assert
        roster.StartedCount.Should().Be(2);
        roster.Count.Should().Be(1);
    }

    [Fact]
    public void AverageWorkingSetBytes_is_nothing_when_nothing_is_running()
        => new WorkerRoster("host").AverageWorkingSetBytes().Should().Be(0L);

    [Fact]
    public void AverageWorkingSetBytes_is_the_mean_of_what_the_running_workers_use()
    {
        //Arrange
        var roster = new WorkerRoster("host");
        var first = Worker("host-1", 1, Noon);
        first.WorkingSetBytes = 100L;
        var second = Worker("host-2", 2, Noon);
        second.WorkingSetBytes = 300L;
        roster.Add(first);
        roster.Add(second);

        //Act, Assert
        roster.AverageWorkingSetBytes().Should().Be(200L);
    }

    [Fact]
    public void AverageWorkingSetBytes_leaves_out_a_worker_whose_use_could_not_be_read()
    {
        //Arrange
        var roster = new WorkerRoster("host");
        var readable = Worker("host-1", 1, Noon);
        readable.WorkingSetBytes = 500L;
        var unreadable = Worker("host-2", 2, Noon);
        unreadable.WorkingSetBytes = 0L;
        roster.Add(readable);
        roster.Add(unreadable);

        //Act, Assert - counting the unreadable one as nothing would halve the figure and let the Hive
        //start twice as many Workers as the host has room for.
        roster.AverageWorkingSetBytes().Should().Be(500L);
    }

    [Fact]
    public void Reap_reports_each_ended_worker_once_and_lets_go_of_it()
    {
        //Arrange
        var roster = new WorkerRoster("host");
        var worker = Worker("host-1", 4242, Noon);
        roster.Add(worker);
        worker.Exit(0);

        //Act
        var first = roster.Reap(Noon.AddSeconds(5), TimeSpan.FromSeconds(10));
        var second = roster.Reap(Noon.AddSeconds(6), TimeSpan.FromSeconds(10));

        //Assert
        first.Should().HaveCount(1);
        first[0].WorkerId.Should().Be("host-1");
        first[0].ProcessId.Should().Be(4242);
        first[0].ExitCode.Should().Be(0);
        first[0].Ran.Should().Be(TimeSpan.FromSeconds(5));
        first[0].IsFailure.Should().BeFalse();
        worker.IsDisposed.Should().BeTrue();
        second.Should().BeEmpty();
    }

    [Fact]
    public void Reap_marks_a_failure_within_the_window_as_an_immediate_one()
    {
        //Arrange - two rosters, so that each Worker is judged at its own moment.
        var quickRoster = new WorkerRoster("host");
        var quick = Worker("host-1", 1, Noon);
        quickRoster.Add(quick);
        quick.Exit(70);

        var slowRoster = new WorkerRoster("host");
        var slow = Worker("host-1", 2, Noon);
        slowRoster.Add(slow);
        slow.Exit(70);

        //Act - one is judged three seconds in, the other half a minute in.
        var immediate = quickRoster.Reap(Noon.AddSeconds(3), TimeSpan.FromSeconds(10));
        var later = slowRoster.Reap(Noon.AddSeconds(30), TimeSpan.FromSeconds(10));

        //Assert
        immediate[0].IsFailure.Should().BeTrue();
        immediate[0].WasImmediateFailure.Should().BeTrue();
        later[0].IsFailure.Should().BeTrue();
        later[0].WasImmediateFailure.Should().BeFalse();
    }

    [Fact]
    public void Reap_reports_the_workers_in_the_order_they_were_started()
    {
        //Arrange
        var roster = new WorkerRoster("host");

        for (var index = 1; index <= 4; index++)
        {
            var worker = Worker("host-" + index, index, Noon);
            roster.Add(worker);
            worker.Exit(0);
        }

        //Act
        var exits = roster.Reap(Noon.AddSeconds(1), TimeSpan.FromSeconds(10));

        //Assert
        exits.Should().HaveCount(4);
        exits[0].WorkerId.Should().Be("host-1");
        exits[3].WorkerId.Should().Be("host-4");
    }

    [Fact]
    public void Reap_never_reports_a_negative_run_length()
    {
        //Arrange - a clock that was set back while a Worker was running.
        var roster = new WorkerRoster("host");
        var worker = Worker("host-1", 1, Noon);
        roster.Add(worker);
        worker.Exit(0);

        //Act
        var exits = roster.Reap(Noon.AddMinutes(-5), TimeSpan.FromSeconds(10));

        //Assert
        exits[0].Ran.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CloseAllLifelines_asks_every_running_worker_to_wind_itself_down()
    {
        //Arrange
        var roster = new WorkerRoster("host");
        var first = Worker("host-1", 1, Noon);
        var second = Worker("host-2", 2, Noon);
        roster.Add(first);
        roster.Add(second);

        //Act
        roster.CloseAllLifelines();

        //Assert
        first.IsLifelineClosed.Should().BeTrue();
        second.IsLifelineClosed.Should().BeTrue();
        roster.AllHaveExited().Should().BeTrue();
    }

    [Fact]
    public void ForceStopAll_stops_only_what_is_still_running()
    {
        //Arrange - one Worker ignores the polite request; the other has already gone.
        var roster = new WorkerRoster("host");
        var stubborn = Worker("host-1", 1, Noon);
        stubborn.EndsWhenLifelineCloses = false;
        var obliging = Worker("host-2", 2, Noon);
        roster.Add(stubborn);
        roster.Add(obliging);
        roster.CloseAllLifelines();

        //Act
        var stopped = roster.ForceStopAll();

        //Assert
        stopped.Should().Be(1);
        stubborn.WasForceStopped.Should().BeTrue();
        obliging.WasForceStopped.Should().BeFalse();
        roster.AllHaveExited().Should().BeTrue();
    }

    [Fact]
    public void Clear_lets_go_of_everything_left()
    {
        //Arrange
        var roster = new WorkerRoster("host");
        var worker = Worker("host-1", 1, Noon);
        roster.Add(worker);

        //Act
        roster.Clear();

        //Assert
        roster.Count.Should().Be(0);
        worker.IsDisposed.Should().BeTrue();
    }
}
