namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Turns two sets of processor counters into a percentage. Every operating system reports the
/// processor as running totals of time spent in this state and that, so the only way to a percentage
/// is the difference between two readings - and every one of them uses counters that can stand still
/// or start again from nothing. The arithmetic is here, on its own, where it can be checked without
/// an operating system underneath it.
/// </summary>
internal static class CpuBusyMath
{
    /// <summary>
    /// The percentage of an interval that was busy, given how much of it was busy and how long it
    /// was altogether.
    /// </summary>
    /// <param name="busyDelta">How much of the interval was spent doing something.</param>
    /// <param name="totalDelta">How long the interval was, in the same units.</param>
    /// <returns>
    /// A percentage between zero and a hundred. Zero when the interval was empty, which is what an
    /// interval too short to measure looks like.
    /// </returns>
    public static double Percent(ulong busyDelta, ulong totalDelta)
    {
        if (totalDelta == 0UL)
        {
            return 0d;
        }

        if (busyDelta > totalDelta)
        {
            busyDelta = totalDelta;
        }

        return busyDelta * 100d / totalDelta;
    }

    /// <summary>
    /// The difference between two readings of a counter that only ever grows, and that reads as zero
    /// again when the thing behind it was started afresh.
    /// </summary>
    /// <param name="previous">The earlier reading.</param>
    /// <param name="current">The later reading.</param>
    /// <returns>
    /// The difference, or zero when the counter went backwards - which means it was started again,
    /// and the interval cannot be measured from these two readings.
    /// </returns>
    public static ulong Difference(ulong previous, ulong current)
        => current >= previous ? current - previous : 0UL;

    /// <summary>
    /// The difference between two readings of a 32-bit counter that wraps round when it fills up,
    /// which is what the processor tick counters on some systems do.
    /// </summary>
    /// <param name="previous">The earlier reading.</param>
    /// <param name="current">The later reading.</param>
    /// <returns>The difference, counting the wrap.</returns>
    public static ulong WrappingDifference(uint previous, uint current)
        => unchecked(current - previous);
}
