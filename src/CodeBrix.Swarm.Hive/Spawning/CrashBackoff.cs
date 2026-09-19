using System;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// How long a Hive waits before starting another Worker, after one has failed almost as soon as it
/// started. Starting Worker after Worker that cannot survive its first seconds achieves nothing and
/// keeps the host busy doing it, so each immediate failure doubles the wait, up to a ceiling.
/// </summary>
/// <remarks>
/// <para>
/// ONLY AN IMMEDIATE FAILURE COUNTS. A Worker that exits with zero has done what it was for, whenever
/// it did it. A Worker that fails after running for a while has at least got somewhere, and the next
/// one might get further. It is the Worker that fails before it has done anything - a missing file, a
/// configuration the work cannot use, a library that will not load - that is worth waiting after,
/// because the next one will fail the same way.
/// </para>
/// <para>
/// And any Worker that survives long enough clears the slate: whatever was wrong has evidently
/// stopped being wrong, so the next start should not be held back by a run that went badly an hour
/// ago.
/// </para>
/// </remarks>
internal sealed class CrashBackoff
{
    //Stands in for the code a Worker that never ran would have exited with. Any value but zero would
    //do; this one is only ever used inside this class.
    private const int FailedStartExitCode = 1;

    private readonly TimeSpan _first;
    private readonly TimeSpan _maximum;
    private readonly TimeSpan _immediateFailureWindow;
    private readonly TimeSpan _resetAfter;

    private TimeSpan _current = TimeSpan.Zero;
    private DateTime _resumeAtUtc = DateTime.MinValue;

    public CrashBackoff(
        TimeSpan first,
        TimeSpan maximum,
        TimeSpan immediateFailureWindow,
        TimeSpan resetAfter)
    {
        _first = first;
        _maximum = maximum < first ? first : maximum;
        _immediateFailureWindow = immediateFailureWindow;
        _resetAfter = resetAfter;
    }

    /// <summary>
    /// The wait the last immediate failure set, or nothing when none is in force.
    /// </summary>
    public TimeSpan Current => _current;

    /// <summary>
    /// Takes account of a Worker that has ended.
    /// </summary>
    /// <param name="nowUtc">The moment it was noticed.</param>
    /// <param name="ran">How long it ran.</param>
    /// <param name="exitCode">The code it ended with.</param>
    /// <returns>True when this ending counted as an immediate failure and lengthened the wait.</returns>
    public bool RecordExit(DateTime nowUtc, TimeSpan ran, int exitCode)
    {
        if (ran >= _resetAfter)
        {
            Reset();
            return false;
        }

        if (SwarmExitCodes.IsSuccess(exitCode) || ran >= _immediateFailureWindow)
        {
            return false;
        }

        _current = _current <= TimeSpan.Zero
            ? _first
            : TimeSpan.FromTicks(_current.Ticks * 2L);

        if (_current > _maximum)
        {
            _current = _maximum;
        }

        _resumeAtUtc = nowUtc + _current;
        return true;
    }

    /// <summary>
    /// Takes account of a Worker that could not be started at all - a program that is not there, a
    /// host that refused it. That is the same situation as an immediate failure, and worse: the next
    /// attempt will fail for the same reason, at once, for as long as nobody puts it right.
    /// </summary>
    /// <param name="nowUtc">The moment the start was refused.</param>
    public void RecordFailedStart(DateTime nowUtc)
        => RecordExit(nowUtc, TimeSpan.Zero, FailedStartExitCode);

    /// <summary>
    /// How much of the wait is still to run at this moment, or nothing when there is none.
    /// </summary>
    /// <param name="nowUtc">The moment to measure from.</param>
    /// <returns>What is left of the wait.</returns>
    public TimeSpan RemainingAt(DateTime nowUtc)
        => _resumeAtUtc <= nowUtc ? TimeSpan.Zero : _resumeAtUtc - nowUtc;

    /// <summary>Forgets every failure so far, and any wait still to run.</summary>
    public void Reset()
    {
        _current = TimeSpan.Zero;
        _resumeAtUtc = DateTime.MinValue;
    }
}
