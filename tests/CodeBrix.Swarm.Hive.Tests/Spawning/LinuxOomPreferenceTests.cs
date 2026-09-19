using System;
using System.Globalization;
using System.IO;
using CodeBrix.Swarm.Hive.Spawning;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests.Spawning;

/// <summary>
/// Asking the Linux kernel to lose a Worker rather than anything else if the host runs out of memory.
/// It is best effort by design, so most of what there is to check is that it never gets in the way.
/// </summary>
public class LinuxOomPreferenceTests
{
    [Fact]
    public void PathFor_names_the_file_the_kernel_publishes_for_that_process()
        => LinuxOomPreference.PathFor(4242).Should().Be("/proc/4242/oom_score_adj");

    [Fact]
    public void the_figure_makes_a_worker_a_preferred_choice_rather_than_a_protected_one()
    {
        //Arrange, Act, Assert - the kernel's range runs from a thousand below zero to a thousand above,
        //and anything above zero means "more likely than ordinary".
        LinuxOomPreference.PreferredVictimScore.Should().BeGreaterThan(0);
        LinuxOomPreference.PreferredVictimScore.Should().BeLessThan(1000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryPrefer_refuses_a_process_number_that_cannot_be_one(int processId)
        => LinuxOomPreference.TryPrefer(processId).Should().BeFalse();

    [Fact]
    public void TryPrefer_says_no_rather_than_throwing_for_a_process_that_is_not_there()
    {
        //Arrange - a number no process on any host will have.
        //Act
        var marked = LinuxOomPreference.TryPrefer(int.MaxValue);

        //Assert
        marked.Should().BeFalse();
    }

    [Fact]
    public void TryPrefer_marks_this_very_process_when_this_host_is_a_linux_one()
    {
        //Arrange - a process may always make ITSELF more likely to be chosen, so this needs no
        //privileges and no child process to try it on.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var self = Environment.ProcessId;
        var path = LinuxOomPreference.PathFor(self);
        var before = File.ReadAllText(path).Trim();

        try
        {
            //Act
            var marked = LinuxOomPreference.TryPrefer(self);

            //Assert
            marked.Should().BeTrue();
            File.ReadAllText(path).Trim().Should()
                .Be(LinuxOomPreference.PreferredVictimScore.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            //Put it back, so that the rest of this suite runs in a process the kernel regards the way
            //it did before.
            LinuxOomPreference.TryPrefer(self, int.Parse(before, CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void TryPrefer_is_simply_no_on_a_host_that_is_not_a_linux_one()
    {
        //Arrange
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        //Act, Assert
        LinuxOomPreference.TryPrefer(Environment.ProcessId).Should().BeFalse();
    }
}
