using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

/// <summary>
/// The Linux probe against files the test wrote, and - on a Linux host - against the real ones. The
/// parsing has its own checks; these are about the probe: which file it reads, what it does with two
/// readings in a row, and what it reports when a file is not there.
/// </summary>
public class LinuxHostLoadProbeTests : IDisposable
{
    private readonly string _folder;
    private readonly string _memInfoPath;
    private readonly string _statPath;

    public LinuxHostLoadProbeTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "swarm-host-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _memInfoPath = Path.Combine(_folder, "meminfo");
        _statPath = Path.Combine(_folder, "stat");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception)
        {
            //A test's own temporary folder that will not go is nothing to fail a test over.
        }
    }

    [Fact]
    public async Task ReadAsync_reports_the_memory_figures_from_the_files()
    {
        //Arrange
        await WriteAsync("MemTotal: 8000000 kB\nMemAvailable: 2000000 kB\n", "cpu  10 0 10 80 0 0 0 0\n");
        var probe = new LinuxHostLoadProbe(_memInfoPath, _statPath);

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reading.IsUsable.Should().BeTrue();
        reading.TotalRamBytes.Should().Be(8000000L * 1024L);
        reading.AvailableRamBytes.Should().Be(2000000L * 1024L);
        reading.UsedRamPercent.Should().Be(75d);
    }

    [Fact]
    public async Task ReadAsync_reports_nothing_busy_on_the_very_first_reading()
    {
        //Arrange - there is nothing to compare the processor counters with yet.
        await WriteAsync("MemTotal: 100 kB\nMemAvailable: 50 kB\n", "cpu  50 0 0 50 0 0 0 0\n");
        var probe = new LinuxHostLoadProbe(_memInfoPath, _statPath);

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reading.CpuBusyPercent.Should().Be(0d);
    }

    [Fact]
    public async Task ReadAsync_works_the_processor_figure_out_from_the_change_since_the_last_reading()
    {
        //Arrange
        await WriteAsync("MemTotal: 100 kB\nMemAvailable: 50 kB\n", "cpu  0 0 0 0 0 0 0 0\n");
        var probe = new LinuxHostLoadProbe(_memInfoPath, _statPath);

        await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Half of the next interval is busy.
        await WriteAsync("MemTotal: 100 kB\nMemAvailable: 50 kB\n", "cpu  50 0 0 50 0 0 0 0\n");

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reading.CpuBusyPercent.Should().Be(50d);
    }

    [Fact]
    public async Task ReadAsync_reports_the_host_as_unmeasurable_when_the_memory_file_is_missing()
    {
        //Arrange - nothing was written, so neither file exists.
        var probe = new LinuxHostLoadProbe(_memInfoPath, _statPath);

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert - a Hive that cannot measure its host does not start Workers on it.
        reading.IsUsable.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_still_reports_the_memory_when_the_processor_file_is_missing()
    {
        //Arrange - the memory figures are the ones that decide whether a Worker starts.
        await File.WriteAllTextAsync(
            _memInfoPath,
            "MemTotal: 100 kB\nMemAvailable: 40 kB\n",
            TestContext.Current.CancellationToken);

        var probe = new LinuxHostLoadProbe(_memInfoPath, _statPath);

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reading.IsUsable.Should().BeTrue();
        reading.CpuBusyPercent.Should().Be(0d);
    }

    [Fact]
    public async Task ReadAsync_measures_this_host_when_this_host_is_a_linux_one()
    {
        //Arrange - the one check that reads what the kernel of the machine under the test really writes.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var probe = new LinuxHostLoadProbe();

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reading.IsUsable.Should().BeTrue();
        reading.TotalRamBytes.Should().BeGreaterThan(0L);
        reading.AvailableRamBytes.Should().BeGreaterThan(0L);
        reading.AvailableRamBytes.Should().BeLessThanOrEqualTo(reading.TotalRamBytes);
        reading.UsedRamPercent.Should().BeGreaterThanOrEqualTo(0d);
        reading.UsedRamPercent.Should().BeLessThanOrEqualTo(100d);
    }

    [Fact]
    public async Task ForThisHost_returns_a_probe_that_measures_this_host()
    {
        //Arrange
        var probe = HostLoadProbes.ForThisHost();

        //Act
        var reading = await probe.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reading.IsUsable.Should().BeTrue();
    }

    private async Task WriteAsync(string memInfo, string stat)
    {
        await File.WriteAllTextAsync(_memInfoPath, memInfo, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_statPath, stat, TestContext.Current.CancellationToken);
    }
}
