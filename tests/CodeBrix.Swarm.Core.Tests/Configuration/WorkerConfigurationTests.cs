using System.Text.Json;
using CodeBrix.Swarm.Core.Configuration;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests.Configuration;

public class WorkerConfigurationTests
{
    private static WorkerConfiguration Complete(string work = null) => new()
    {
        Swarm = new WorkerSwarmSettings
        {
            QueenUrl = "http://127.0.0.1:5000",
            Token = "an-opaque-token",
            WorkerId = "worker-3"
        },
        Work = work
    };

    [Fact]
    public void ToJsonLine_writes_one_line()
    {
        //Act
        var json = Complete().ToJsonLine();

        //Assert
        json.Should().NotContain("\n");
        json.Should().StartWith("{");
    }

    [Fact]
    public void Parse_reads_back_what_ToJsonLine_wrote()
    {
        //Arrange
        var written = Complete("{\"batch\":4}");

        //Act
        var read = WorkerConfiguration.Parse(written.ToJsonLine());

        //Assert
        read.Swarm.QueenUrl.Should().Be("http://127.0.0.1:5000");
        read.Swarm.Token.Should().Be("an-opaque-token");
        read.Swarm.WorkerId.Should().Be("worker-3");
    }

    [Fact]
    public void the_application_part_travels_through_unread()
    {
        //Arrange - an object the swarm knows nothing about, with nested values and an array.
        const string work = "{\"name\":\"a batch\",\"sizes\":[1,2,3],\"nested\":{\"deep\":true},\"none\":null}";

        //Act
        var read = WorkerConfiguration.Parse(Complete(work).ToJsonLine());

        //Assert
        JsonDocument.Parse(read.Work).RootElement.GetProperty("name").GetString().Should().Be("a batch");
        JsonDocument.Parse(read.Work).RootElement.GetProperty("sizes").GetArrayLength().Should().Be(3);
        JsonDocument.Parse(read.Work).RootElement.GetProperty("nested").GetProperty("deep").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public void the_application_part_may_be_any_json_value()
    {
        //Act
        var read = WorkerConfiguration.Parse(Complete("[1,2,3]").ToJsonLine());

        //Assert
        read.Work.Should().Be("[1,2,3]");
    }

    [Fact]
    public void a_configuration_with_no_application_part_is_fine()
    {
        //Act
        var read = WorkerConfiguration.Parse(Complete().ToJsonLine());

        //Assert
        read.Work.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_refuses_an_empty_line(string json)
    {
        //Act
        var act = () => WorkerConfiguration.Parse(json);

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Parse_refuses_text_that_is_not_json()
    {
        //Act
        var act = () => WorkerConfiguration.Parse("this is not json");

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Parse_refuses_json_null()
    {
        //Act
        var act = () => WorkerConfiguration.Parse("null");

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_a_configuration_with_no_swarm_part()
    {
        //Arrange
        var configuration = new WorkerConfiguration();

        //Act
        var act = configuration.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData(null, "token", "worker-1")]
    [InlineData("http://127.0.0.1:5000", null, "worker-1")]
    [InlineData("http://127.0.0.1:5000", "token", null)]
    [InlineData("  ", "token", "worker-1")]
    public void Validate_refuses_a_swarm_part_with_something_missing(
        string queenUrl, string token, string workerId)
    {
        //Arrange
        var configuration = new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings { QueenUrl = queenUrl, Token = token, WorkerId = workerId }
        };

        //Act
        var act = configuration.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_refuses_an_address_that_is_not_absolute()
    {
        //Arrange
        var configuration = Complete();
        configuration.Swarm.QueenUrl = "not-an-address";

        //Act
        var act = configuration.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Theory]
    [InlineData("/swarm/worker")]
    [InlineData("file:///swarm/worker")]
    [InlineData("ws://127.0.0.1:5000")]
    public void Validate_refuses_an_address_that_is_absolute_but_not_http_or_https(string queenUrl)
    {
        //Arrange - a bare path is an ABSOLUTE FILE ADDRESS on Linux. Refusing it here is what makes a
        //Worker exit with the code for a configuration it cannot use.
        var configuration = Complete();
        configuration.Swarm.QueenUrl = queenUrl;

        //Act
        var act = configuration.Validate;

        //Assert
        act.Should().Throw<SwarmConfigurationException>();
    }

    [Fact]
    public void Validate_accepts_an_https_address()
    {
        //Arrange
        var configuration = Complete();
        configuration.Swarm.QueenUrl = "https://swarm.example:5001";

        //Act
        var act = configuration.Validate;

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_accepts_a_complete_configuration()
    {
        //Act
        var act = Complete().Validate;

        //Assert
        act.Should().NotThrow();
    }
}
