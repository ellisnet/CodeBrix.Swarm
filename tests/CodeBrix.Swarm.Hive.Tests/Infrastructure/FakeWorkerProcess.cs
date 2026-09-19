using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Hive.Spawning;

namespace CodeBrix.Swarm.Hive.Tests.Infrastructure;

/// <summary>
/// A Worker that was never started: it remembers everything that was asked of it and ends when the test
/// says so.
/// </summary>
internal sealed class FakeWorkerProcess : IWorkerProcess
{
    /// <summary>
    /// The code a Worker that had to be stopped reports. The real value depends on the host; all that
    /// matters here is that it is not zero.
    /// </summary>
    public const int ForcedStopExitCode = 137;

    private readonly TaskCompletionSource _exited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeWorkerProcess(WorkerProcessRequest request, int processId, DateTime startedUtc)
    {
        Request = request;
        ProcessId = processId;
        StartedUtc = startedUtc;
    }

    /// <summary>What the Hive asked to be started, configuration and all.</summary>
    public WorkerProcessRequest Request { get; }

    public string WorkerId => Request.WorkerId;

    public int ProcessId { get; }

    public DateTime StartedUtc { get; }

    public bool HasExited { get; private set; }

    public int ExitCode { get; private set; }

    public long WorkingSetBytes { get; set; }

    /// <summary>True once the Hive closed the lifeline.</summary>
    public bool IsLifelineClosed { get; private set; }

    /// <summary>True once the Hive stopped this Worker the hard way.</summary>
    public bool WasForceStopped { get; private set; }

    /// <summary>True once the Hive let go of this Worker.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Whether closing the lifeline ends this Worker, as it would a well-behaved one. A test turns it
    /// off to describe a Worker that ignores the polite request and has to be stopped.
    /// </summary>
    public bool EndsWhenLifelineCloses { get; set; } = true;

    /// <summary>Ends the Worker with a code of the test's choosing.</summary>
    public void Exit(int exitCode)
    {
        if (HasExited)
        {
            return;
        }

        ExitCode = exitCode;
        HasExited = true;
        _exited.TrySetResult();
    }

    public void CloseLifeline()
    {
        IsLifelineClosed = true;

        if (EndsWhenLifelineCloses)
        {
            Exit(0);
        }
    }

    public void ForceStop()
    {
        WasForceStopped = true;
        Exit(ForcedStopExitCode);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => _exited.Task.WaitAsync(cancellationToken);

    public void Dispose() => IsDisposed = true;
}
