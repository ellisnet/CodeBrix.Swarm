using CodeBrix.Swarm.Hive.HostLoad;
using CodeBrix.Swarm.Hive.Spawning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.Spawning;

public class SpawnAdmissionTests
{
    private const long Gibibyte = 1024L * 1024L * 1024L;

    private static HostLoadReading Host(long totalGibibytes, long availableGibibytes, double busyPercent = 0d)
        => new(totalGibibytes * Gibibyte, availableGibibytes * Gibibyte, busyPercent);

    private static SwarmHostLimits Limits() => new();

    [Fact]
    public void Evaluate_admits_the_first_worker_on_a_quiet_host()
    {
        //Arrange - nothing running yet, so there is no footprint to learn from.
        //Act
        var refusal = SpawnAdmission.Evaluate(Host(64L, 48L), 0d, 0, 0L, Limits());

        //Assert
        refusal.Should().Be(SpawnRefusal.None);
    }

    [Fact]
    public void Evaluate_refuses_when_the_hive_already_has_as_many_workers_as_it_may()
    {
        //Arrange
        var limits = Limits();
        limits.MaxWorkers = 5;

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(64L, 48L), 0d, 5, Gibibyte, limits);

        //Assert
        refusal.Should().Be(SpawnRefusal.AtWorkerLimit);
    }

    [Fact]
    public void Evaluate_admits_one_more_while_the_hive_is_below_its_worker_limit()
    {
        //Arrange
        var limits = Limits();
        limits.MaxWorkers = 5;

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(64L, 48L), 0d, 4, Gibibyte, limits);

        //Assert
        refusal.Should().Be(SpawnRefusal.None);
    }

    [Fact]
    public void Evaluate_refuses_when_no_limit_on_workers_is_set_but_the_host_is_full()
    {
        //Arrange - no maximum number of Workers is the default; the host is the limit.
        var limits = Limits();
        limits.MaxWorkers.Should().BeNull();

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(64L, 1L), 0d, 20, 512L * 1024L * 1024L, limits);

        //Assert
        refusal.Should().NotBe(SpawnRefusal.None);
    }

    [Fact]
    public void Evaluate_judges_the_host_as_it_would_be_after_the_next_worker_rather_than_as_it_is()
    {
        //Arrange - a host with twelve gibibytes available, a floor of ten, and Workers that have turned
        //out to need three each. As it stands there is room; once another Worker has taken its share
        //there would be nine gibibytes left, which is below the floor. THIS is what keeps a Hive from
        //starting far too many Workers before the first reading catches up with the first one.
        var limits = Limits();
        limits.FreeRamFloorBytes = 10L * Gibibyte;

        //Act
        var asItIs = SpawnAdmission.Evaluate(Host(64L, 12L), 0d, 1, 0L, limits);
        var asItWouldBe = SpawnAdmission.Evaluate(Host(64L, 12L), 0d, 1, 3L * Gibibyte, limits);

        //Assert
        asItIs.Should().Be(SpawnRefusal.None);
        asItWouldBe.Should().Be(SpawnRefusal.FreeRamFloorReached);
    }

    [Fact]
    public void Evaluate_refuses_when_the_floor_would_be_broken_however_low_the_percentage_is()
    {
        //Arrange - a small host, four gibibytes altogether, with half a gibibyte available. That is
        //under ninety percent in use, so the percentage says there is room - and half a gibibyte is not
        //room for anything. A percentage alone is no use on a host of an unknown size, which is why
        //there is a floor as well.
        var limits = Limits();
        limits.FreeRamFloorBytes = Gibibyte;

        var reading = new HostLoadReading(4L * Gibibyte, Gibibyte / 2L, 0d);

        //Act
        var refusal = SpawnAdmission.Evaluate(reading, 0d, 0, 0L, limits);
        var percentageAlone = reading.UsedRamPercent;

        //Assert
        percentageAlone.Should().BeLessThan(limits.MaxRamPercent);
        refusal.Should().Be(SpawnRefusal.FreeRamFloorReached);
    }

    [Fact]
    public void Evaluate_refuses_when_the_share_of_memory_would_be_passed()
    {
        //Arrange - a hundred gibibytes, eleven available, and no floor in the way. Another Worker of
        //two gibibytes would leave nine available, which is ninety-one percent in use.
        var limits = Limits();
        limits.FreeRamFloorBytes = 0L;
        limits.MaxRamPercent = 90d;

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(100L, 11L), 0d, 1, 2L * Gibibyte, limits);

        //Assert
        refusal.Should().Be(SpawnRefusal.RamPercentReached);
    }

    [Fact]
    public void Evaluate_respects_a_lower_share_of_memory_than_the_default()
    {
        //Arrange - an application that wants to be left half the host.
        var limits = Limits();
        limits.FreeRamFloorBytes = 0L;
        limits.MaxRamPercent = 50d;

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(100L, 40L), 0d, 0, 0L, limits);

        //Assert - sixty percent in use is past what it asked for.
        refusal.Should().Be(SpawnRefusal.RamPercentReached);
    }

    [Fact]
    public void Evaluate_refuses_when_the_processor_has_been_busier_than_its_share()
    {
        //Arrange
        var limits = Limits();

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(64L, 48L), 95d, 1, Gibibyte, limits);

        //Assert
        refusal.Should().Be(SpawnRefusal.CpuPercentReached);
    }

    [Fact]
    public void Evaluate_looks_at_the_average_processor_figure_and_not_the_latest_reading()
    {
        //Arrange - the host read as entirely busy the instant it was measured, but has been quiet over
        //the window, which is the figure that decides.
        var limits = Limits();

        //Act
        var refusal = SpawnAdmission.Evaluate(Host(64L, 48L, 100d), 4d, 1, Gibibyte, limits);

        //Assert
        refusal.Should().Be(SpawnRefusal.None);
    }

    [Fact]
    public void Evaluate_refuses_when_the_host_could_not_be_measured()
    {
        //Act
        var refusal = SpawnAdmission.Evaluate(HostLoadReading.Unknown, 0d, 0, 0L, Limits());

        //Assert
        refusal.Should().Be(SpawnRefusal.HostLoadUnknown);
    }

    [Fact]
    public void Evaluate_refuses_when_there_is_no_reading_at_all()
        => SpawnAdmission.Evaluate(null, 0d, 0, 0L, Limits()).Should().Be(SpawnRefusal.HostLoadUnknown);

    [Fact]
    public void Evaluate_puts_the_worker_limit_before_a_host_it_cannot_measure()
    {
        //Arrange - being at the limit is certain whether or not the host can be read.
        var limits = Limits();
        limits.MaxWorkers = 2;

        //Act
        var refusal = SpawnAdmission.Evaluate(HostLoadReading.Unknown, 0d, 2, 0L, limits);

        //Assert
        refusal.Should().Be(SpawnRefusal.AtWorkerLimit);
    }
}
