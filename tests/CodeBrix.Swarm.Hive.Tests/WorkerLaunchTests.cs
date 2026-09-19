using System;
using System.Text.Json;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Hive.Tests;

public class WorkerLaunchTests
{
    private sealed class Assignment
    {
        public string Piece { get; set; }

        public int Count { get; set; }
    }

    [Fact]
    public void NotNow_carries_nothing_to_start()
    {
        //Act
        var launch = WorkerLaunch.NotNow;

        //Assert
        launch.IsNotNow.Should().BeTrue();
        launch.Executable.Should().BeNull();
        launch.Arguments.Should().BeEmpty();
        launch.Work.Should().BeNull();
    }

    [Fact]
    public void Start_carries_the_program_to_start()
    {
        //Act
        var launch = WorkerLaunch.Start("/opt/work/worker");

        //Assert
        launch.IsNotNow.Should().BeFalse();
        launch.Executable.Should().Be("/opt/work/worker");
        launch.Arguments.Should().BeEmpty();
        launch.Work.Should().BeNull();
    }

    [Fact]
    public void Start_trims_the_program_name()
        => WorkerLaunch.Start("  /opt/work/worker  ").Executable.Should().Be("/opt/work/worker");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Start_refuses_a_launch_with_nothing_to_start(string executable)
    {
        //Arrange
        var start = () => WorkerLaunch.Start(executable);

        //Act, Assert
        start.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Start_keeps_each_argument_separate()
    {
        //Arrange - handed to the operating system one at a time, so a path with a space in it needs no
        //quoting and no quoting rule has to be guessed at.
        //Act
        var launch = WorkerLaunch.Start("worker", ["--mode=long", "/a path/with spaces"]);

        //Assert
        launch.Arguments.Should().ContainInOrder("--mode=long", "/a path/with spaces");
    }

    [Fact]
    public void Start_takes_a_copy_of_the_arguments()
    {
        //Arrange
        var arguments = new[] { "first" };

        //Act
        var launch = WorkerLaunch.Start("worker", arguments);
        arguments[0] = "changed afterwards";

        //Assert
        launch.Arguments[0].Should().Be("first");
    }

    [Fact]
    public void Start_leaves_out_an_argument_that_is_nothing_at_all()
    {
        //Act
        var launch = WorkerLaunch.Start("worker", ["first", null, "second"]);

        //Assert
        launch.Arguments.Should().ContainInOrder("first", "second");
        launch.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void Start_carries_json_through_exactly_as_it_was_given()
    {
        //Arrange - the swarm does not read the application's part, so it must not rewrite it either.
        const string work = "{\"piece\":\"seventeen\",\"count\":3}";

        //Act
        var launch = WorkerLaunch.Start("worker", [], work);

        //Assert
        launch.Work.Should().Be(work);
    }

    [Fact]
    public void StartWithWork_writes_the_value_to_json()
    {
        //Act
        var launch = WorkerLaunch.StartWithWork(
            "worker", [], new Assignment { Piece = "seventeen", Count = 3 });

        //Assert
        launch.Work.Should().NotBeNull();

        var read = JsonSerializer.Deserialize<Assignment>(
            launch.Work,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        read.Piece.Should().Be("seventeen");
        read.Count.Should().Be(3);
    }

    [Fact]
    public void StartWithWork_sends_nothing_for_a_value_that_is_nothing()
        => WorkerLaunch
            .StartWithWork<Assignment>("worker", [], null)
            .Work
            .Should()
            .BeNull();

    [Fact]
    public void WorkingDirectory_is_nowhere_in_particular_unless_it_is_set()
    {
        //Act
        var launch = WorkerLaunch.Start("worker");

        //Assert
        launch.WorkingDirectory.Should().BeNull();

        //Act
        launch.WorkingDirectory = "/opt/work";

        //Assert
        launch.WorkingDirectory.Should().Be("/opt/work");
    }

    [Fact]
    public void NotNow_is_always_the_same_answer()
        //Nothing distinguishes one "not now" from another, so there is one of them.
        => WorkerLaunch.NotNow.Should().BeSameAs(WorkerLaunch.NotNow);
}
