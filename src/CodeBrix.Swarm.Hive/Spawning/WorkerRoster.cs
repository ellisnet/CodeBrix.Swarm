using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// The Workers a Hive has running: what they are called, how much memory they have turned out to
/// need, which of them have ended, and how to end the rest.
/// </summary>
/// <remarks>
/// The average footprint this keeps is the whole reason a Hive can start Workers without
/// oversubscribing a host. The first Worker is a guess; from then on the Hive knows, from its own
/// Workers on this host, roughly what the next one is going to cost.
/// </remarks>
internal sealed class WorkerRoster
{
    private readonly string _hiveId;
    private readonly List<IWorkerProcess> _running = [];
    private readonly object _gate = new();

    private int _started;

    public WorkerRoster(string hiveId) => _hiveId = hiveId;

    /// <summary>How many Workers are running.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _running.Count;
            }
        }
    }

    /// <summary>How many Workers this Hive has started altogether.</summary>
    public int StartedCount
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    /// <summary>
    /// A name for the next Worker, different from every one this Hive has used.
    /// </summary>
    /// <returns>The name.</returns>
    public string NextWorkerId()
    {
        lock (_gate)
        {
            return _hiveId + "-" + (_started + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Takes a newly started Worker into the roster.
    /// </summary>
    /// <param name="worker">The Worker.</param>
    public void Add(IWorkerProcess worker)
    {
        ArgumentNullException.ThrowIfNull(worker);

        lock (_gate)
        {
            _running.Add(worker);
            _started++;
        }
    }

    /// <summary>
    /// What a Worker of this Hive's has turned out to cost, in bytes, averaged over the ones running,
    /// or zero when none is.
    /// </summary>
    public long AverageWorkingSetBytes()
    {
        IWorkerProcess[] running;

        lock (_gate)
        {
            if (_running.Count == 0)
            {
                return 0L;
            }

            running = _running.ToArray();
        }

        var total = 0L;
        var counted = 0;

        foreach (var worker in running)
        {
            var bytes = worker.WorkingSetBytes;

            if (bytes <= 0L)
            {
                continue;
            }

            total += bytes;
            counted++;
        }

        return counted == 0 ? 0L : total / counted;
    }

    /// <summary>
    /// Finds the Workers that have ended, takes them out of the roster and lets go of them.
    /// </summary>
    /// <param name="nowUtc">The moment this is being done, for working out how long each one ran.</param>
    /// <param name="immediateFailureWindow">
    /// How soon after starting a failure counts as an immediate one.
    /// </param>
    /// <returns>One record per Worker that had ended, in the order they were started.</returns>
    public IReadOnlyList<WorkerExit> Reap(DateTime nowUtc, TimeSpan immediateFailureWindow)
    {
        List<IWorkerProcess> ended = null;

        lock (_gate)
        {
            for (var index = _running.Count - 1; index >= 0; index--)
            {
                var worker = _running[index];

                if (!worker.HasExited)
                {
                    continue;
                }

                _running.RemoveAt(index);
                ended ??= [];
                ended.Add(worker);
            }
        }

        if (ended == null)
        {
            return [];
        }

        //Taken out from the end, so put back in the order they were started.
        ended.Reverse();

        var exits = new List<WorkerExit>(ended.Count);

        foreach (var worker in ended)
        {
            var ran = nowUtc - worker.StartedUtc;

            if (ran < TimeSpan.Zero)
            {
                ran = TimeSpan.Zero;
            }

            var exitCode = worker.ExitCode;

            exits.Add(new WorkerExit(
                worker.WorkerId,
                worker.ProcessId,
                exitCode,
                ran,
                exitCode != 0 && ran < immediateFailureWindow));

            worker.Dispose();
        }

        return exits;
    }

    /// <summary>
    /// Closes the lifeline of every Worker running, which is how a Hive asks them all to wind
    /// themselves down.
    /// </summary>
    public void CloseAllLifelines()
    {
        foreach (var worker in CurrentWorkers())
        {
            worker.CloseLifeline();
        }
    }

    /// <summary>
    /// Stops every Worker that is still running, and everything they started.
    /// </summary>
    /// <returns>How many had to be stopped this way.</returns>
    public int ForceStopAll()
    {
        var stopped = 0;

        foreach (var worker in CurrentWorkers())
        {
            if (worker.HasExited)
            {
                continue;
            }

            worker.ForceStop();
            stopped++;
        }

        return stopped;
    }

    /// <summary>True when every Worker in the roster has ended.</summary>
    public bool AllHaveExited()
    {
        foreach (var worker in CurrentWorkers())
        {
            if (!worker.HasExited)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lets go of every Worker left in the roster, whatever state it is in. Called last of all.
    /// </summary>
    public void Clear()
    {
        IWorkerProcess[] running;

        lock (_gate)
        {
            running = _running.ToArray();
            _running.Clear();
        }

        foreach (var worker in running)
        {
            worker.Dispose();
        }
    }

    /// <summary>
    /// The Workers in the roster at this instant, as an array that nothing else can change while it
    /// is being walked.
    /// </summary>
    private IWorkerProcess[] CurrentWorkers()
    {
        lock (_gate)
        {
            return _running.ToArray();
        }
    }
}
