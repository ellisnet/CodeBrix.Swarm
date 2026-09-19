using System;
using System.Collections.Generic;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// The last so many seconds of processor readings, and their average. A single reading of a host's
/// processor is nearly worthless for deciding anything - every host reads as busy for the instant
/// something wakes up on it - so the figure a Hive compares with its limit is an average over a
/// window.
/// </summary>
/// <remarks>
/// An empty window averages to nothing, which means a Hive that has only just started is not held
/// back by a figure it has not measured yet. Its first reading arrives immediately afterwards.
/// </remarks>
internal sealed class CpuAverageWindow
{
    private readonly TimeSpan _window;
    private readonly Queue<Sample> _samples = new();
    private readonly object _gate = new();

    public CpuAverageWindow(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(window), window, "The averaging window must be longer than nothing.");
        }

        _window = window;
    }

    /// <summary>How many readings are in the window at the moment.</summary>
    public int SampleCount
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count;
            }
        }
    }

    /// <summary>
    /// Adds a reading. Anything older than the window is let go of at the same time.
    /// </summary>
    /// <param name="takenUtc">When the reading was taken.</param>
    /// <param name="busyPercent">How busy the processor was, as a percentage.</param>
    public void Add(DateTime takenUtc, double busyPercent)
    {
        if (double.IsNaN(busyPercent))
        {
            return;
        }

        lock (_gate)
        {
            _samples.Enqueue(new Sample(takenUtc, busyPercent));
            DropOldSamples(takenUtc);
        }
    }

    /// <summary>
    /// The average of the readings still inside the window, or zero when there are none.
    /// </summary>
    /// <param name="nowUtc">The moment to measure the window back from.</param>
    /// <returns>The average percentage.</returns>
    public double Average(DateTime nowUtc)
    {
        lock (_gate)
        {
            DropOldSamples(nowUtc);

            if (_samples.Count == 0)
            {
                return 0d;
            }

            var total = 0d;

            foreach (var sample in _samples)
            {
                total += sample.BusyPercent;
            }

            return total / _samples.Count;
        }
    }

    private void DropOldSamples(DateTime nowUtc)
    {
        var oldest = nowUtc - _window;

        while (_samples.Count > 0 && _samples.Peek().TakenUtc < oldest)
        {
            _samples.Dequeue();
        }
    }

    private readonly struct Sample
    {
        public Sample(DateTime takenUtc, double busyPercent)
        {
            TakenUtc = takenUtc;
            BusyPercent = busyPercent;
        }

        public DateTime TakenUtc { get; }

        public double BusyPercent { get; }
    }
}
