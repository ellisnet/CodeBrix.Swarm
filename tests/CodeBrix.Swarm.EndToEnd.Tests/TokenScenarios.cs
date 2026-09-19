using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Connections;
using CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.EndToEnd.Tests;

/// <summary>
/// A real coordinator, and real connections presenting tokens it should and should not let in. The rule
/// these scenarios exist for: A KEY THAT WORKS FOR A HIVE MUST NOT WORK FOR A WORKER, AND THE OTHER WAY
/// ROUND. Everything else about a swarm's arrangement rests on that being true in practice and not only
/// in principle.
/// </summary>
public class TokenScenarios
{
    //A short window: every one of these is expected to be refused, and a refusal is retried like any
    //other failure until the window runs out.
    private static readonly TimeSpan ShortWindow = TimeSpan.FromMilliseconds(600);

    private static SwarmConnectionOptions Connection(string baseUrl, string token)
        => new()
        {
            QueenUrl = baseUrl,
            Token = token,
            QueenUnreachableWindow = ShortWindow,
            FirstRetryDelay = TimeSpan.FromMilliseconds(100),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(200)
        };

    private static async Task<bool> WasLetInAsync(
        string baseUrl,
        SwarmRole role,
        string token,
        CancellationToken cancellationToken)
    {
        await using var client = new SwarmHubClient(role, Connection(baseUrl, token));

        var connected = await client.StartAsync(cancellationToken);

        if (connected)
        {
            await client.StopAsync(cancellationToken);
        }

        return connected;
    }

    [Fact]
    public async Task a_hive_token_is_refused_at_the_worker_hub()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var hiveToken = scenario.Queen.CreateHiveToken();

        //Act
        var atItsOwnHub = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Hive, hiveToken, cancellationToken);

        var atTheWrongHub = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Worker, hiveToken, cancellationToken);

        //Assert
        atItsOwnHub.Should().BeTrue();
        atTheWrongHub.Should().BeFalse();

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_worker_token_is_refused_at_the_hive_hub()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var workerToken = scenario.Queen.CreateWorkerToken();

        //Act
        var atItsOwnHub = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Worker, workerToken, cancellationToken);

        var atTheWrongHub = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Hive, workerToken, cancellationToken);

        //Assert
        atItsOwnHub.Should().BeTrue();
        atTheWrongHub.Should().BeFalse();

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Theory]
    [InlineData("rubbish")]
    [InlineData("bm90LWEtdG9rZW4tYXQtYWxsLWJ1dC1kb2VzLWRlY29kZQ")]
    [InlineData("!!! not even an encoding !!!")]
    public async Task a_token_that_is_not_one_at_all_is_refused(string token)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        //Act
        var letIn = await WasLetInAsync(scenario.QueenUrl, SwarmRole.Hive, token, cancellationToken);

        //Assert
        letIn.Should().BeFalse();
        scenario.Queen.HiveCount.Should().Be(0);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task an_expired_token_is_refused()
    {
        //Arrange - a token that is good for one second, presented after that second has passed. A token
        //cannot be minted already expired: the coordinator refuses to make one, which is itself worth
        //saying here.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var mintOneAlreadyExpired = () => scenario.Queen.CreateHiveToken(TimeSpan.FromMinutes(-5));
        mintOneAlreadyExpired.Should().Throw<ArgumentOutOfRangeException>();

        var shortLived = scenario.Queen.CreateHiveToken(TimeSpan.FromSeconds(1));

        //Act - good while it lasts.
        var whileItLasted = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Hive, shortLived, cancellationToken);

        //An expiry is recorded to the second, so a whole second past it leaves no room for doubt.
        await Task.Delay(TimeSpan.FromMilliseconds(2500), cancellationToken);

        var afterwards = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Hive, shortLived, cancellationToken);

        //Assert
        whileItLasted.Should().BeTrue();
        afterwards.Should().BeFalse();
        scenario.Queen.HiveCount.Should().Be(0);

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    [Fact]
    public async Task a_token_that_has_been_meddled_with_is_refused()
    {
        //Arrange - one character of a perfectly good token changed. The token is sealed as well as
        //encrypted, so it does not decrypt at all afterwards.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await SwarmScenario.StartAsync(cancellationToken);

        var good = scenario.Queen.CreateHiveToken();
        var meddled = Meddle(good);

        //Act
        var theGoodOne = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Hive, good, cancellationToken);

        var theMeddledOne = await WasLetInAsync(
            scenario.QueenUrl, SwarmRole.Hive, meddled, cancellationToken);

        //Assert
        meddled.Should().NotBe(good);
        meddled.Length.Should().Be(good.Length);
        theGoodOne.Should().BeTrue();
        theMeddledOne.Should().BeFalse();

        await SwarmScenario.AssertNoWorkerProcessesAreLeftAsync(cancellationToken);
    }

    private static string Meddle(string token)
    {
        var characters = token.ToCharArray();
        var index = characters.Length / 2;
        characters[index] = characters[index] == 'A' ? 'B' : 'A';

        return new string(characters);
    }
}
