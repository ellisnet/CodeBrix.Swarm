using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Swarm.Worker.Startup;
using CodeBrix.Swarm.Worker.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Worker.Tests.Startup;

public class WorkerLifelineTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task the_end_of_the_pipe_is_reported()
    {
        //Arrange
        var pipe = new PipeTextReader();
        var closed = new TaskCompletionSource();
        using var lifeline = new WorkerLifeline(pipe);
        lifeline.Closed += (_, _) => closed.TrySetResult();
        lifeline.Start();

        //Act
        pipe.EndOfPipe();

        //Assert
        await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_pipe_that_is_still_open_is_not_reported()
    {
        //Arrange
        var pipe = new PipeTextReader();
        var closed = false;
        using var lifeline = new WorkerLifeline(pipe);
        lifeline.Closed += (_, _) => closed = true;
        lifeline.Start();

        //Act - something arrives on the pipe, and then nothing for a while.
        pipe.WriteLine("still here");
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        //Assert
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task a_pipe_that_was_already_at_its_end_is_reported_at_once()
    {
        //Arrange
        var closed = new TaskCompletionSource();
        using var lifeline = new WorkerLifeline(TextReader.Null);
        lifeline.Closed += (_, _) => closed.TrySetResult();

        //Act
        lifeline.Start();

        //Assert
        await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task nothing_is_reported_after_the_lifeline_has_been_put_down()
    {
        //Arrange
        var pipe = new PipeTextReader();
        var closed = false;
        var lifeline = new WorkerLifeline(pipe);
        lifeline.Closed += (_, _) => closed = true;
        lifeline.Start();

        //Act
        lifeline.Dispose();
        pipe.EndOfPipe();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        //Assert
        closed.Should().BeFalse();
    }

    [Fact]
    public void a_lifeline_needs_something_to_watch()
    {
        //Act
        var act = () => new WorkerLifeline(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
