using System;
using System.Threading;

namespace CodeBrix.Swarm.Hive.Running;

/// <summary>
/// Records why a Hive is finishing, and stops it when something decides it should. Several things can
/// decide at once - the coordinator saying the work is over just as the connection goes for good - so
/// the first reason recorded is the one that is reported.
/// </summary>
internal sealed class HiveEnding
{
    private readonly CancellationTokenSource _source;

    private int _reason;

    public HiveEnding(CancellationTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>True once something has asked the Hive to finish.</summary>
    public bool IsRequested => Volatile.Read(ref _reason) != 0;

    /// <summary>
    /// The first reason recorded. A run that somehow ends with nothing recorded is reported as having
    /// been ended from outside, which is the only way that can happen.
    /// </summary>
    public SwarmHiveOutcome Outcome
    {
        get
        {
            var reason = Volatile.Read(ref _reason);
            return reason == 0 ? SwarmHiveOutcome.Cancelled : (SwarmHiveOutcome)reason;
        }
    }

    /// <summary>
    /// Records a reason - the first one wins - and stops the Hive.
    /// </summary>
    /// <param name="outcome">Why the Hive should finish.</param>
    public void Request(SwarmHiveOutcome outcome)
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
    /// Records a reason without stopping anything, for a reason discovered when the Hive is already on
    /// its way out.
    /// </summary>
    /// <param name="outcome">Why the Hive finished.</param>
    public void Record(SwarmHiveOutcome outcome)
        => Interlocked.CompareExchange(ref _reason, (int)outcome, 0);
}
