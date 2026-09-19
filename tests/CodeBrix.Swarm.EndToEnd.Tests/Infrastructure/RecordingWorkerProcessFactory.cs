using System.Collections.Generic;
using CodeBrix.Swarm.Hive.Spawning;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Starts real Worker processes, exactly as a Hive does, and keeps a record of every one and of what it
/// was told. The record is how these scenarios check afterwards that each Worker was handed the right
/// configuration, and which process numbers to look for when checking that none of them is left.
/// </summary>
internal sealed class RecordingWorkerProcessFactory : IWorkerProcessFactory
{
    private readonly IWorkerProcessFactory _inner;
    private readonly List<StartedWorker> _started = [];
    private readonly object _gate = new();

    public RecordingWorkerProcessFactory()
        : this(WorkerProcessFactory.Instance) { }

    public RecordingWorkerProcessFactory(IWorkerProcessFactory inner) => _inner = inner;

    /// <summary>Every Worker that was started, in the order it was.</summary>
    public IReadOnlyList<StartedWorker> Started
    {
        get
        {
            lock (_gate)
            {
                return _started.ToArray();
            }
        }
    }

    /// <summary>How many Workers were started altogether.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _started.Count;
            }
        }
    }

    public IWorkerProcess Start(WorkerProcessRequest request)
    {
        var worker = _inner.Start(request);

        lock (_gate)
        {
            _started.Add(new StartedWorker(request, worker.ProcessId));
        }

        return worker;
    }
}
