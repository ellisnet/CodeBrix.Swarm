using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Diagnostics;

namespace CodeBrix.Swarm.Hive.Tests.Infrastructure;

/// <summary>
/// A clock that never sleeps: waiting for a span moves the time forward by that span and returns
/// straight away, and every span asked for is recorded. A minute-long retry window therefore runs to
/// its end in microseconds, and the schedule of waits can be asserted on.
/// </summary>
internal sealed class FakeSwarmClock : ISwarmClock
{
    private readonly object _gate = new();
    private readonly List<TimeSpan> _delays = [];

    private DateTime _utcNow;

    public FakeSwarmClock()
        : this(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)) { }

    public FakeSwarmClock(DateTime startUtc) => _utcNow = startUtc;

    public DateTime UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_gate)
            {
                return _delays.ToArray();
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _delays.Add(delay);
            _utcNow += delay;
        }

        return Task.CompletedTask;
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _utcNow += by;
        }
    }
}
