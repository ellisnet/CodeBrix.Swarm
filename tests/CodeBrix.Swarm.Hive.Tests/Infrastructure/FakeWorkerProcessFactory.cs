using System;
using System.Collections.Generic;
using CodeBrix.Swarm.Core.Diagnostics;
using CodeBrix.Swarm.Hive.Spawning;

namespace CodeBrix.Swarm.Hive.Tests.Infrastructure;

/// <summary>
/// Hands out <see cref="FakeWorkerProcess" /> instances and keeps every one it made, so a test can see
/// exactly what each Worker was told and when it was started. The start times come from the test's own
/// clock, which is what makes a Hive's pacing and its waits after failures observable.
/// </summary>
internal sealed class FakeWorkerProcessFactory : IWorkerProcessFactory
{
    private readonly ISwarmClock _clock;
    private readonly List<FakeWorkerProcess> _started = [];
    private readonly object _gate = new();

    private int _nextProcessId = 1000;

    public FakeWorkerProcessFactory(ISwarmClock clock) => _clock = clock;

    /// <summary>Every Worker this factory has handed out, in the order it did.</summary>
    public IReadOnlyList<FakeWorkerProcess> Started
    {
        get
        {
            lock (_gate)
            {
                return _started.ToArray();
            }
        }
    }

    /// <summary>The one handed out most recently, or null when none has been.</summary>
    public FakeWorkerProcess Latest
    {
        get
        {
            lock (_gate)
            {
                return _started.Count == 0 ? null : _started[^1];
            }
        }
    }

    /// <summary>
    /// What each Worker reports using, in bytes. The default is a modest amount, so that a Hive with a
    /// generous host in front of it is not held back by the footprint it learns.
    /// </summary>
    public long WorkingSetBytes { get; set; } = 256L * 1024L * 1024L;

    /// <summary>
    /// Set to refuse to start anything, the way a program that is not there would.
    /// </summary>
    public bool RefuseToStart { get; set; }

    /// <summary>
    /// Called with each Worker just after it was handed out, for a test that wants it to end at once or
    /// to ignore the polite request later.
    /// </summary>
    public Action<FakeWorkerProcess> OnStarted { get; set; }

    public IWorkerProcess Start(WorkerProcessRequest request)
    {
        if (RefuseToStart)
        {
            throw new InvalidOperationException("This factory was told to refuse to start anything.");
        }

        FakeWorkerProcess worker;

        lock (_gate)
        {
            worker = new FakeWorkerProcess(request, _nextProcessId++, _clock.UtcNow)
            {
                WorkingSetBytes = WorkingSetBytes
            };

            _started.Add(worker);
        }

        OnStarted?.Invoke(worker);

        return worker;
    }
}
