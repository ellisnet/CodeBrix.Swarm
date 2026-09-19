using System;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests;

public class SwarmHiveOptionsTests
{
    private static SwarmHiveOptions Usable() => new()
    {
        QueenUrl = "http://127.0.0.1:5000",
        HiveToken = "a-token-for-this-hive",
        WorkerToken = "a-token-for-its-workers",
        NextWorkerAsync = (_, _) => Task.FromResult(WorkerLaunch.NotNow)
    };

    [Fact]
    public void Validate_accepts_the_least_that_will_do()
    {
        //Arrange
        var validate = () => Usable().Validate();

        //Act, Assert
        validate.Should().NotThrow();
    }

    [Fact]
    public void the_defaults_are_the_ones_the_design_calls_for()
    {
        //Arrange
        var options = new SwarmHiveOptions();

        //Assert
        options.QueenUnreachableWindow.Should().Be(TimeSpan.FromSeconds(60));
        options.SettleInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.NotNowInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.ImmediateFailureWindow.Should().Be(TimeSpan.FromSeconds(10));
        options.FirstBackoff.Should().Be(TimeSpan.FromSeconds(5));
        options.MaximumBackoff.Should().Be(TimeSpan.FromMinutes(5));
        options.BackoffResetAfter.Should().Be(TimeSpan.FromMinutes(1));
        options.TerminationGracePeriod.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void collecting_what_the_workers_write_is_off_to_begin_with()
        //A redirected stream that nobody reads fills up and stops the process writing to it. Off is the
        //arrangement that cannot go wrong.
        => new SwarmHiveOptions().CaptureWorkerOutput.Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("/swarm/hive")]
    public void Validate_refuses_a_coordinator_address_it_cannot_use(string url)
    {
        //Arrange
        var options = Usable();
        options.QueenUrl = url;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_refuses_options_with_no_token_for_the_hive_itself(string token)
    {
        //Arrange
        var options = Usable();
        options.HiveToken = token;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_refuses_options_with_no_token_to_pass_to_the_workers(string token)
    {
        //Arrange - traffic in a swarm is one-way, so a Hive cannot ask the coordinator for one. It is
        //handed both tokens when it starts or it cannot do its job.
        var options = Usable();
        options.WorkerToken = token;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_hive_that_has_not_been_told_what_to_start()
    {
        //Arrange
        var options = Usable();
        options.NextWorkerAsync = null;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_options_with_the_limits_taken_away()
    {
        //Arrange
        var options = Usable();
        options.Limits = null;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_limits_above_the_ceiling()
    {
        //Arrange
        var options = Usable();
        options.Limits.MaxRamPercent = 95d;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_negative_window_for_reaching_the_coordinator()
    {
        //Arrange
        var options = Usable();
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(-1);

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_accepts_a_window_of_nothing_for_reaching_the_coordinator()
    {
        //Arrange - no patience at all is a legitimate thing to ask for: one attempt, and give up.
        var options = Usable();
        options.QueenUnreachableWindow = TimeSpan.Zero;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_refuses_a_longest_retry_delay_shorter_than_the_first_one()
    {
        //Arrange
        var options = Usable();
        options.FirstRetryDelay = TimeSpan.FromSeconds(5);
        options.MaximumRetryDelay = TimeSpan.FromSeconds(1);

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_ceiling_on_the_waits_below_the_first_one()
    {
        //Arrange
        var options = Usable();
        options.FirstBackoff = TimeSpan.FromMinutes(2);
        options.MaximumBackoff = TimeSpan.FromSeconds(30);

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_negative_grace_period()
    {
        //Arrange
        var options = Usable();
        options.TerminationGracePeriod = TimeSpan.FromSeconds(-1);

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_accepts_a_grace_period_of_nothing()
    {
        //Arrange - closing the lifelines and stopping whatever has not gone by the next instant is a
        //legitimate thing to ask for.
        var options = Usable();
        options.TerminationGracePeriod = TimeSpan.Zero;

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData("SettleInterval")]
    [InlineData("NotNowInterval")]
    [InlineData("PollInterval")]
    [InlineData("ImmediateFailureWindow")]
    [InlineData("FirstBackoff")]
    [InlineData("BackoffResetAfter")]
    public void Validate_refuses_an_interval_of_no_length(string which)
    {
        //Arrange
        var options = Usable();

        switch (which)
        {
            case "SettleInterval":
                options.SettleInterval = TimeSpan.Zero;
                break;
            case "NotNowInterval":
                options.NotNowInterval = TimeSpan.Zero;
                break;
            case "PollInterval":
                options.PollInterval = TimeSpan.Zero;
                break;
            case "ImmediateFailureWindow":
                options.ImmediateFailureWindow = TimeSpan.Zero;
                break;
            case "FirstBackoff":
                options.FirstBackoff = TimeSpan.Zero;
                break;
            default:
                options.BackoffResetAfter = TimeSpan.Zero;
                break;
        }

        //Act, Assert
        var validate = () => options.Validate();
        validate.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void ResolveHiveId_uses_the_hosts_own_name_when_none_was_given()
    {
        //Arrange
        var options = Usable();

        //Act
        var hiveId = SwarmHiveHost.ResolveHiveId(options);

        //Assert
        hiveId.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void ResolveHiveId_uses_the_name_it_was_given_and_trims_it()
    {
        //Arrange
        var options = Usable();
        options.HiveId = "  host-seventeen  ";

        //Act, Assert
        SwarmHiveHost.ResolveHiveId(options).Should().Be("host-seventeen");
    }

    [Fact]
    public void ExitCodeFor_maps_each_ending_to_the_code_a_process_should_exit_with()
    {
        //Assert
        SwarmHiveHost.ExitCodeFor(SwarmHiveOutcome.ToldToTerminate).Should().Be(SwarmExitCodes.Success);
        SwarmHiveHost.ExitCodeFor(SwarmHiveOutcome.Cancelled).Should().Be(SwarmExitCodes.Success);
        SwarmHiveHost.ExitCodeFor(SwarmHiveOutcome.QueenUnreachable).Should()
            .Be(SwarmExitCodes.QueenUnreachable);
        SwarmHiveHost.ExitCodeFor(SwarmHiveOutcome.Failed).Should().Be(SwarmExitCodes.WorkFailed);
    }

    [Fact]
    public void ResolveLimits_lowers_the_limits_the_environment_asks_it_to()
    {
        //Arrange
        var options = Usable();

        //Act
        var limits = SwarmHiveHost.ResolveLimits(
            options,
            name => name == SwarmHostLimits.MaxWorkersVariable ? "2" : null);

        //Assert
        limits.MaxWorkers.Should().Be(2);
        options.Limits.MaxWorkers.Should().BeNull();
    }
}
