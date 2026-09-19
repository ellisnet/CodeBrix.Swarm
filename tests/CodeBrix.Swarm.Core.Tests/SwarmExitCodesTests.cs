using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests;

public class SwarmExitCodesTests
{
    [Fact]
    public void an_ordinary_end_is_zero()
        => SwarmExitCodes.Success.Should().Be(0);

    [Fact]
    public void every_failure_has_its_own_code()
    {
        //Arrange
        var codes = new List<int>
        {
            SwarmExitCodes.Success,
            SwarmExitCodes.ConfigurationInvalid,
            SwarmExitCodes.QueenUnreachable,
            SwarmExitCodes.WorkFailed
        };

        //Assert
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void the_failure_codes_stay_clear_of_the_codes_a_shell_produces()
    {
        //Assert - 126, 127 and 128 and above belong to the shell and to signals.
        SwarmExitCodes.ConfigurationInvalid.Should().BeLessThan(126);
        SwarmExitCodes.QueenUnreachable.Should().BeLessThan(126);
        SwarmExitCodes.WorkFailed.Should().BeLessThan(126);
        SwarmExitCodes.ConfigurationInvalid.Should().BeGreaterThan(0);
        SwarmExitCodes.QueenUnreachable.Should().BeGreaterThan(0);
        SwarmExitCodes.WorkFailed.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(69, false)]
    public void IsSuccess_only_accepts_zero(int exitCode, bool expected)
        => SwarmExitCodes.IsSuccess(exitCode).Should().Be(expected);
}
