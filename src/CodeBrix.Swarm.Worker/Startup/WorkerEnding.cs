using System;
using System.Threading;

namespace CodeBrix.Swarm.Worker.Startup;

/// <summary>
/// Records why a Worker is stopping, and cancels the work when something decides it should.
/// Several things can decide at once - a terminate message arriving just as the lifeline closes -
/// so the first reason recorded is the one that is reported.
/// </summary>
internal sealed class WorkerEnding
{
    private readonly CancellationTokenSource _source;

    private int _reason;

    public WorkerEnding(CancellationTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>True once something has asked the Worker to stop.</summary>
    public bool IsRequested => Volatile.Read(ref _reason) != 0;

    /// <summary>
    /// The first reason recorded, or <see cref="SwarmWorkerOutcome.WorkFinished" /> when nothing
    /// asked the Worker to stop and the work simply ended.
    /// </summary>
    public SwarmWorkerOutcome Outcome
    {
        get
        {
            var reason = Volatile.Read(ref _reason);
            return reason == 0 ? SwarmWorkerOutcome.WorkFinished : (SwarmWorkerOutcome)reason;
        }
    }

    /// <summary>
    /// Records a reason - the first one wins - and cancels the work.
    /// </summary>
    /// <param name="outcome">Why the Worker should stop.</param>
    public void Request(SwarmWorkerOutcome outcome)
    {
        Interlocked.CompareExchange(ref _reason, (int)outcome, 0);

        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            //The run is already over.
        }
    }

    /// <summary>
    /// Records a reason without cancelling anything, for a reason discovered after the work has
    /// already ended.
    /// </summary>
    /// <param name="outcome">Why the Worker stopped.</param>
    public void Record(SwarmWorkerOutcome outcome)
        => Interlocked.CompareExchange(ref _reason, (int)outcome, 0);
}
