using System.IO;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.Worker.Startup;
using CodeBrix.Swarm.Worker.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Worker.Tests.Startup;

public class WorkerConfigurationReaderTests
{
    private static string CompleteJson(string work = null) => new WorkerConfiguration
    {
        Swarm = new WorkerSwarmSettings
        {
            QueenUrl = "http://127.0.0.1:5000",
            Token = "an-opaque-token",
            WorkerId = "worker-1"
        },
        Work = work
    }.ToJsonLine();

    [Fact]
    public async Task ReadAsync_takes_the_first_line_of_the_pipe()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson("{\"batch\":2}"));

        //Act
        var startup = await WorkerConfigurationReader.ReadAsync([], pipe, TestContext.Current.CancellationToken);

        //Assert
        startup.Configuration.Swarm.WorkerId.Should().Be("worker-1");
        startup.Configuration.Work.Should().Be("{\"batch\":2}");
        startup.IsDevelopmentMode.Should().BeFalse();
        startup.ConfigurationFilePath.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_leaves_the_rest_of_the_pipe_alone()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        pipe.WriteLine("something the Worker never reads as configuration");

        //Act
        await WorkerConfigurationReader.ReadAsync([], pipe, TestContext.Current.CancellationToken);
        var next = await pipe.ReadLineAsync(TestContext.Current.CancellationToken);

        //Assert
        next.Should().Be("something the Worker never reads as configuration");
    }

    [Fact]
    public async Task ReadAsync_refuses_a_pipe_that_ends_with_nothing_on_it()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.EndOfPipe();

        //Act
        var act = async () => await WorkerConfigurationReader.ReadAsync(
            [], pipe, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<SwarmConfigurationException>();
    }

    [Fact]
    public async Task ReadAsync_refuses_a_line_that_is_not_json()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine("this is not json");

        //Act
        var act = async () => await WorkerConfigurationReader.ReadAsync(
            [], pipe, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<SwarmConfigurationException>();
    }

    [Fact]
    public async Task ReadAsync_refuses_a_configuration_with_something_missing()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine("{\"swarm\":{\"queenUrl\":\"http://127.0.0.1:5000\"}}");

        //Act
        var act = async () => await WorkerConfigurationReader.ReadAsync(
            [], pipe, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<SwarmConfigurationException>();
    }

    [Fact]
    public async Task ReadAsync_reads_the_file_named_with_the_swarm_config_switch_instead()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        await File.WriteAllTextAsync(path, CompleteJson("{\"batch\":9}"), TestContext.Current.CancellationToken);

        try
        {
            //Act
            var startup = await WorkerConfigurationReader.ReadAsync(
                ["--swarm-config", path], TextReader.Null, TestContext.Current.CancellationToken);

            //Assert
            startup.IsDevelopmentMode.Should().BeTrue();
            startup.ConfigurationFilePath.Should().Be(path);
            startup.Configuration.Work.Should().Be("{\"batch\":9}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_reads_the_file_attached_to_the_swarm_config_switch()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        await File.WriteAllTextAsync(path, CompleteJson(), TestContext.Current.CancellationToken);

        try
        {
            //Act
            var startup = await WorkerConfigurationReader.ReadAsync(
                ["--swarm-config=" + path], TextReader.Null, TestContext.Current.CancellationToken);

            //Assert
            startup.IsDevelopmentMode.Should().BeTrue();
            startup.ConfigurationFilePath.Should().Be(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_refuses_a_file_that_is_not_there()
    {
        //Arrange
        var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        //Act
        var act = async () => await WorkerConfigurationReader.ReadAsync(
            ["--swarm-config", missing], TextReader.Null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<SwarmConfigurationException>();
    }

    [Fact]
    public async Task ReadAsync_still_takes_the_pipe_when_the_application_has_its_own_arguments()
    {
        //Arrange - THE POINT OF THE NARROW SWITCH. A consuming application writes whatever command line
        //it likes, and none of it can put a Worker into development mode by accident.
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson("{\"batch\":4}"));

        //Act
        var startup = await WorkerConfigurationReader.ReadAsync(
            ["--tenant", "acme", "/data/input.csv", "--verbose"],
            pipe,
            TestContext.Current.CancellationToken);

        //Assert
        startup.IsDevelopmentMode.Should().BeFalse();
        startup.ConfigurationFilePath.Should().BeNull();
        startup.Configuration.Work.Should().Be("{\"batch\":4}");
    }

    [Fact]
    public void FindConfigurationPath_finds_the_value_after_the_switch()
        => WorkerConfigurationReader.FindConfigurationPath(["--quiet", "--swarm-config", "/tmp/one.json"])
            .Should().Be("/tmp/one.json");

    [Fact]
    public void FindConfigurationPath_finds_the_value_attached_with_an_equals_sign()
        => WorkerConfigurationReader.FindConfigurationPath(["--swarm-config=/tmp/one.json"])
            .Should().Be("/tmp/one.json");

    [Fact]
    public void FindConfigurationPath_finds_nothing_when_there_are_no_arguments()
        => WorkerConfigurationReader.FindConfigurationPath([]).Should().BeNull();

    [Fact]
    public void FindConfigurationPath_ignores_an_argument_that_is_not_a_switch()
        => WorkerConfigurationReader.FindConfigurationPath(["/tmp/one.json", "/tmp/two.json"])
            .Should().BeNull();

    [Fact]
    public void FindConfigurationPath_ignores_the_applications_own_config_switch()
        => WorkerConfigurationReader.FindConfigurationPath(["--config", "/tmp/theirs.json"])
            .Should().BeNull();

    [Fact]
    public void FindConfigurationPath_ignores_the_applications_own_configuration_switch()
        => WorkerConfigurationReader.FindConfigurationPath(["--configuration", "Release"])
            .Should().BeNull();

    [Fact]
    public void FindConfigurationPath_ignores_a_switch_that_only_starts_the_same_way()
        => WorkerConfigurationReader.FindConfigurationPath(["--swarm-configuration", "/tmp/one.json"])
            .Should().BeNull();

    [Fact]
    public void FindConfigurationPath_finds_nothing_when_the_arguments_are_all_switches()
        => WorkerConfigurationReader.FindConfigurationPath(["--quiet", "--verbose"]).Should().BeNull();

    [Fact]
    public void FindConfigurationPath_refuses_the_switch_with_no_file_after_it()
    {
        //Act
        var act = () => WorkerConfigurationReader.FindConfigurationPath(["--swarm-config"]);

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void FindConfigurationPath_refuses_the_switch_with_nothing_after_the_equals_sign()
    {
        //Act
        var act = () => WorkerConfigurationReader.FindConfigurationPath(["--swarm-config="]);

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }
}
