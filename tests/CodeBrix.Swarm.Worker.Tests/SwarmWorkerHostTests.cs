using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.Worker.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Swarm.Worker.Tests;

public class SwarmWorkerHostTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

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

    private static SwarmWorkerOptions Options(Func<SwarmWorkerContext, CancellationToken, Task> work)
        => new()
        {
            WorkAsync = work,
            QueenUnreachableWindow = TimeSpan.FromSeconds(30),
            FirstRetryDelay = TimeSpan.FromMilliseconds(250),
            MaximumRetryDelay = TimeSpan.FromSeconds(3)
        };

    [Fact]
    public async Task a_pipe_with_nothing_on_it_ends_the_worker_with_the_configuration_code()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.EndOfPipe();

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            Options((_, _) => Task.CompletedTask),
            [],
            pipe,
            new FakeHubConnectionFactory(),
            new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.ConfigurationInvalid);
    }

    [Fact]
    public async Task a_line_that_is_not_json_ends_the_worker_with_the_configuration_code()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine("this is not json");
        var reported = string.Empty;
        var options = Options((_, _) => Task.CompletedTask);
        options.Report = message => reported = message;

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            options, [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.ConfigurationInvalid);
        reported.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task work_that_finishes_ends_the_worker_with_zero()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var ran = false;

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            Options((_, _) =>
            {
                ran = true;
                return Task.CompletedTask;
            }),
            [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        ran.Should().BeTrue();
        exitCode.Should().Be(SwarmExitCodes.Success);
    }

    [Fact]
    public async Task the_work_is_handed_the_application_part_of_the_configuration_unread()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson("{\"batch\":7,\"name\":\"third\"}"));
        string work = null;
        string workerId = null;

        //Act
        await SwarmWorkerHost.RunAsync(
            Options((context, _) =>
            {
                work = context.Work;
                workerId = context.WorkerId;
                return Task.CompletedTask;
            }),
            [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        work.Should().Be("{\"batch\":7,\"name\":\"third\"}");
        workerId.Should().Be("worker-1");
    }

    [Fact]
    public async Task a_coordinator_that_never_answers_ends_the_worker_with_the_unreachable_code()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var ran = false;
        var options = Options((_, _) =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(60);

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            options, [], pipe, new FakeHubConnectionFactory(_ => true), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.QueenUnreachable);
        ran.Should().BeFalse();
    }

    [Fact]
    public async Task work_that_throws_ends_the_worker_with_the_failure_code()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            Options((_, _) => throw new InvalidOperationException("the work went wrong")),
            [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.WorkFailed);
    }

    [Fact]
    public async Task the_terminate_message_ends_the_work_and_the_worker_exits_normally()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var factory = new FakeHubConnectionFactory();
        var working = new TaskCompletionSource();
        SwarmWorkerOutcome outcome = default;

        var options = Options(async (_, cancellationToken) =>
        {
            working.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        options.ShutdownAsync = (context, _) =>
        {
            outcome = context.Outcome;
            return Task.CompletedTask;
        };

        //Act
        var run = SwarmWorkerHost.RunAsync(
            options, [], pipe, factory, new FakeSwarmClock(), TestContext.Current.CancellationToken);

        await working.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        await factory.Latest.DeliverAsync(SwarmMessage.Create(SwarmMessageKinds.TerminateWorker));
        var exitCode = await run.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.Success);
        outcome.Should().Be(SwarmWorkerOutcome.ToldToTerminate);
    }

    [Fact]
    public async Task the_end_of_the_lifeline_ends_the_work_and_the_worker_exits_normally()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var working = new TaskCompletionSource();
        SwarmWorkerOutcome outcome = default;

        var options = Options(async (_, cancellationToken) =>
        {
            working.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        options.ShutdownAsync = (context, _) =>
        {
            outcome = context.Outcome;
            return Task.CompletedTask;
        };

        //Act
        var run = SwarmWorkerHost.RunAsync(
            options, [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        await working.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        pipe.EndOfPipe();
        var exitCode = await run.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.Success);
        outcome.Should().Be(SwarmWorkerOutcome.LifelineClosed);
    }

    [Fact]
    public async Task a_coordinator_that_goes_away_while_the_work_runs_ends_the_worker_with_the_unreachable_code()
    {
        //Arrange - the first connection opens; every later attempt is refused.
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var factory = new FakeHubConnectionFactory(attempt => attempt > 0);
        var working = new TaskCompletionSource();
        SwarmWorkerOutcome outcome = default;

        var options = Options(async (_, cancellationToken) =>
        {
            working.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        options.QueenUnreachableWindow = TimeSpan.FromSeconds(60);
        options.ShutdownAsync = (context, _) =>
        {
            outcome = context.Outcome;
            return Task.CompletedTask;
        };

        //Act
        var run = SwarmWorkerHost.RunAsync(
            options, [], pipe, factory, new FakeSwarmClock(), TestContext.Current.CancellationToken);

        await working.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        await factory.Created[0].DropAsync(new InvalidOperationException("the coordinator went away"));
        var exitCode = await run.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.QueenUnreachable);
        outcome.Should().Be(SwarmWorkerOutcome.QueenUnreachable);
    }

    [Fact]
    public async Task the_configure_step_runs_before_the_worker_connects()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var factory = new FakeHubConnectionFactory();
        var connectedWhenConfigured = true;

        var options = Options((_, _) => Task.CompletedTask);
        options.ConfigureAsync = (context, _) =>
        {
            connectedWhenConfigured = factory.Created.Count > 0;
            context.RegisterHandler("app.one", (_, _) => Task.CompletedTask);
            return Task.CompletedTask;
        };

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            options, [], pipe, factory, new FakeSwarmClock(), TestContext.Current.CancellationToken);

        //Assert
        connectedWhenConfigured.Should().BeFalse();
        exitCode.Should().Be(SwarmExitCodes.Success);
    }

    [Fact]
    public async Task the_shutdown_step_runs_even_when_the_work_threw()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var shutdownRan = false;

        var options = Options((_, _) => throw new InvalidOperationException("the work went wrong"));
        options.ShutdownAsync = (_, _) =>
        {
            shutdownRan = true;
            return Task.CompletedTask;
        };

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            options, [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        shutdownRan.Should().BeTrue();
        exitCode.Should().Be(SwarmExitCodes.WorkFailed);
    }

    [Fact]
    public async Task a_worker_started_by_hand_from_a_file_runs_with_no_lifeline()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        await File.WriteAllTextAsync(path, CompleteJson("{\"batch\":1}"), TestContext.Current.CancellationToken);
        var development = false;

        try
        {
            //Act - standard input is already at its end, which would end a Worker that had a lifeline.
            var exitCode = await SwarmWorkerHost.RunAsync(
                Options((context, _) =>
                {
                    development = context.IsDevelopmentMode;
                    return Task.CompletedTask;
                }),
                ["--swarm-config", path], TextReader.Null, new FakeHubConnectionFactory(),
                new FakeSwarmClock(), TestContext.Current.CancellationToken);

            //Assert
            development.Should().BeTrue();
            exitCode.Should().Be(SwarmExitCodes.Success);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task an_applications_own_arguments_do_not_put_a_worker_into_development_mode()
    {
        //Arrange - THE NARROW SWITCH, at the level that matters: the whole Worker. The command line
        //belongs to the consuming application, and only --swarm-config reaches the library.
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var development = true;

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            Options((context, _) =>
            {
                development = context.IsDevelopmentMode;
                return Task.CompletedTask;
            }),
            ["--config", "/tmp/theirs.json", "--tenant", "acme", "input.csv"],
            pipe,
            new FakeHubConnectionFactory(),
            new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert - it read the pipe, as a Worker a Hive started always does.
        development.Should().BeFalse();
        exitCode.Should().Be(SwarmExitCodes.Success);
    }

    [Fact]
    public async Task a_coordinator_address_that_is_not_http_ends_the_worker_with_the_configuration_code()
    {
        //Arrange - on Linux a bare path parses as an absolute FILE address, so a Worker handed one has
        //to refuse it rather than spend its whole retry window failing to connect to it.
        var pipe = new PipeTextReader();

        pipe.WriteLine(new WorkerConfiguration
        {
            Swarm = new WorkerSwarmSettings
            {
                QueenUrl = "/swarm/worker",
                Token = "an-opaque-token",
                WorkerId = "worker-1"
            }
        }.ToJsonLine());

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            Options((_, _) => Task.CompletedTask),
            [],
            pipe,
            new FakeHubConnectionFactory(),
            new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.ConfigurationInvalid);
    }

    [Fact]
    public async Task an_application_message_reaches_the_handler_the_application_registered()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        var factory = new FakeHubConnectionFactory();
        var working = new TaskCompletionSource();
        var arrived = new TaskCompletionSource<SwarmMessage>();

        var options = Options(async (_, cancellationToken) =>
        {
            working.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        options.ConfigureAsync = (context, _) =>
        {
            context.RegisterHandler("app.one", (message, _) =>
            {
                arrived.TrySetResult(message);
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        };

        //Act
        var run = SwarmWorkerHost.RunAsync(
            options, [], pipe, factory, new FakeSwarmClock(), TestContext.Current.CancellationToken);

        await working.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        await factory.Latest.DeliverAsync(SwarmMessage.Create("app.one"));
        var delivered = await arrived.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        pipe.EndOfPipe();
        await run.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        //Assert
        delivered.Kind.Should().Be("app.one");
    }

    [Fact]
    public async Task the_application_cannot_register_for_one_of_the_swarm_own_kinds()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());
        Exception caught = null;

        var options = Options((_, _) => Task.CompletedTask);
        options.ConfigureAsync = (context, _) =>
        {
            try
            {
                context.RegisterHandler(SwarmMessageKinds.TerminateWorker, (_, _) => Task.CompletedTask);
            }
            catch (ArgumentException ex)
            {
                caught = ex;
            }

            return Task.CompletedTask;
        };

        //Act
        await SwarmWorkerHost.RunAsync(
            options, [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        caught.Should().NotBeNull();
    }

    [Fact]
    public async Task options_with_no_work_are_refused()
    {
        //Act
        var act = async () => await SwarmWorkerHost.RunAsync(
            new SwarmWorkerOptions(), [], TextReader.Null, new FakeHubConnectionFactory(),
            new FakeSwarmClock(), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<SwarmConfigurationException>();
    }

    [Fact]
    public async Task a_run_with_no_options_at_all_is_refused()
    {
        //Act
        var act = async () => await SwarmWorkerHost.RunAsync(
            null, [], TextReader.Null, new FakeHubConnectionFactory(),
            new FakeSwarmClock(), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetWork_reads_the_application_part_into_a_type_of_its_own()
    {
        //Arrange
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson("{\"batch\":4,\"name\":\"fourth\"}"));
        WorkOrder read = null;

        //Act
        await SwarmWorkerHost.RunAsync(
            Options((context, _) =>
            {
                read = context.GetWork<WorkOrder>();
                return Task.CompletedTask;
            }),
            [], pipe, new FakeHubConnectionFactory(), new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        read.Batch.Should().Be(4);
        read.Name.Should().Be("fourth");
    }

    [Fact]
    public async Task a_worker_ended_while_it_is_still_connecting_exits_with_a_documented_code()
    {
        //Arrange - the lifeline goes while the connection is still opening, which cancels the attempt
        //from underneath itself. A Worker must exit with the code for the reason it was ended; falling
        //out of its own start-up would end the process with whatever the runtime does about an unhandled
        //failure, and tell the Hive watching it something completely untrue.
        var pipe = new PipeTextReader();
        pipe.WriteLine(CompleteJson());

        var reported = new List<string>();
        var options = Options((_, _) => Task.CompletedTask);
        options.Report = message => reported.Add(message);

        SwarmWorkerOutcome? outcome = null;
        options.ShutdownAsync = (context, _) =>
        {
            outcome = context.Outcome;
            return Task.CompletedTask;
        };

        //Act
        var exitCode = await SwarmWorkerHost.RunAsync(
            options,
            [],
            pipe,
            new EndsTheLifelineHubConnectionFactory(pipe.EndOfPipe),
            new FakeSwarmClock(),
            TestContext.Current.CancellationToken);

        //Assert
        exitCode.Should().Be(SwarmExitCodes.Success);
        outcome.Should().Be(SwarmWorkerOutcome.LifelineClosed);
        reported.Should().NotBeEmpty();
    }

    private sealed class WorkOrder
    {
        public int Batch { get; set; }

        public string Name { get; set; }
    }
}
