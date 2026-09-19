using System;
using CodeBrix.Swarm.Core.Messaging;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Core.Tests.Messaging;

public class SwarmMessageKindsTests
{
    [Fact]
    public void the_two_built_in_kinds_are_different_from_each_other()
        => SwarmMessageKinds.TerminateAllWorkers.Should().NotBe(SwarmMessageKinds.TerminateWorker);

    [Theory]
    [InlineData("swarm.terminate-all-workers", true)]
    [InlineData("swarm.terminate-worker", true)]
    [InlineData("swarm.something-later", true)]
    [InlineData("app.terminate-worker", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBuiltIn_recognises_the_reserved_prefix(string kind, bool expected)
        => SwarmMessageKinds.IsBuiltIn(kind).Should().Be(expected);

    [Fact]
    public void TerminateKindFor_gives_a_hive_the_kind_that_ends_all_its_workers()
        => SwarmMessageKinds.TerminateKindFor(SwarmRole.Hive)
            .Should().Be(SwarmMessageKinds.TerminateAllWorkers);

    [Fact]
    public void TerminateKindFor_gives_a_worker_the_kind_that_ends_itself()
        => SwarmMessageKinds.TerminateKindFor(SwarmRole.Worker)
            .Should().Be(SwarmMessageKinds.TerminateWorker);

    [Fact]
    public void TerminateKindFor_refuses_a_role_the_swarm_does_not_know()
    {
        //Act
        var act = () => SwarmMessageKinds.TerminateKindFor((SwarmRole)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
