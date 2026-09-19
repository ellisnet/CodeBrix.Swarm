using CodeBrix.Swarm.Hive.HostLoad;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.HostLoad;

public class LinuxMemInfoTests
{
    //The first lines of the file as a Debian host really writes it, values and spacing included.
    private const string RealFile =
        "MemTotal:       16316148 kB\n"
        + "MemFree:          171724 kB\n"
        + "MemAvailable:    3495876 kB\n"
        + "Buffers:          123456 kB\n"
        + "Cached:          9876543 kB\n"
        + "SwapCached:            0 kB\n"
        + "HugePages_Total:       0\n";

    [Fact]
    public void TryParse_reads_the_total_and_the_available_figure()
    {
        //Act
        var read = LinuxMemInfo.TryParse(RealFile, out var total, out var available);

        //Assert
        read.Should().BeTrue();
        total.Should().Be(16316148L * 1024L);
        available.Should().Be(3495876L * 1024L);
    }

    [Fact]
    public void TryParse_ignores_the_free_figure_entirely()
    {
        //Arrange - a healthy host has almost no free memory and plenty available. Reading the wrong
        //one would make every working host look as though it had none.
        //Act
        LinuxMemInfo.TryParse(RealFile, out _, out var available);

        //Assert
        available.Should().NotBe(171724L * 1024L);
    }

    [Fact]
    public void TryParse_refuses_a_file_with_no_available_figure()
    {
        //Act
        var read = LinuxMemInfo.TryParse("MemTotal:       16316148 kB\nMemFree: 100 kB\n", out var total, out var available);

        //Assert
        read.Should().BeFalse();
        total.Should().Be(0L);
        available.Should().Be(0L);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nothing like the real thing")]
    [InlineData("MemAvailable:    3495876 kB")]
    public void TryParse_refuses_anything_it_cannot_read(string text)
        => LinuxMemInfo.TryParse(text, out _, out _).Should().BeFalse();

    [Fact]
    public void TryParse_never_reports_more_available_than_the_host_has()
    {
        //Arrange - not something a kernel does, but the figure is used in arithmetic either way.
        //Act
        LinuxMemInfo.TryParse("MemTotal: 1000 kB\nMemAvailable: 9999 kB\n", out var total, out var available);

        //Assert
        available.Should().Be(total);
    }

    [Fact]
    public void TryReadLabelledValue_reads_kibibytes_as_bytes()
    {
        //Act
        var read = LinuxMemInfo.TryReadLabelledValue("MemTotal:       16316148 kB", "MemTotal:", out var bytes);

        //Assert
        read.Should().BeTrue();
        bytes.Should().Be(16316148L * 1024L);
    }

    [Fact]
    public void TryReadLabelledValue_reads_a_line_with_no_unit_as_bytes()
    {
        //Act
        var read = LinuxMemInfo.TryReadLabelledValue("HugePages_Total:       7", "HugePages_Total:", out var bytes);

        //Assert
        read.Should().BeTrue();
        bytes.Should().Be(7L);
    }

    [Fact]
    public void TryReadLabelledValue_refuses_a_unit_it_does_not_know()
        => LinuxMemInfo
            .TryReadLabelledValue("MemTotal: 16 furlongs", "MemTotal:", out _)
            .Should()
            .BeFalse();

    [Fact]
    public void TryReadLabelledValue_refuses_a_different_label()
        => LinuxMemInfo
            .TryReadLabelledValue("MemFree: 100 kB", "MemTotal:", out _)
            .Should()
            .BeFalse();

    [Fact]
    public void TryReadLabelledValue_refuses_a_label_with_no_number_after_it()
        => LinuxMemInfo.TryReadLabelledValue("MemTotal:  kB", "MemTotal:", out _).Should().BeFalse();
}
