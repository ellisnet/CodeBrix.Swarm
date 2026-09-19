using System;
using System.Globalization;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Reads the whole-host processor totals out of the text the Linux kernel publishes about the
/// scheduler, and turns two readings of them into a percentage.
/// </summary>
/// <remarks>
/// <para>
/// The first line of the file reads <c>cpu</c> followed by running totals, in clock ticks, of the
/// time every processor together has spent in each state: user, nice, system, idle, iowait, irq,
/// softirq, steal, and then two guest figures. The two guest figures are already counted inside user
/// and nice, so adding them would count that time twice; they are left out.
/// </para>
/// <para>
/// Time spent waiting for a disk counts as idle here rather than busy. A host that is waiting on its
/// disks is not one to load with more Workers, but it is also not one whose processor is the thing
/// standing in the way, and the limit being compared against is a processor limit.
/// </para>
/// </remarks>
internal static class LinuxProcStat
{
    /// <summary>Where the kernel publishes it.</summary>
    public const string Path = "/proc/stat";

    private const string TotalsLinePrefix = "cpu ";

    /// <summary>
    /// Reads the whole-host totals out of the file's text.
    /// </summary>
    /// <param name="text">The whole contents of the file.</param>
    /// <param name="busyTicks">The time spent doing something, in clock ticks.</param>
    /// <param name="totalTicks">The time altogether, busy and idle, in clock ticks.</param>
    /// <returns>True when the line was found and carried numbers.</returns>
    public static bool TryParseTotals(string text, out ulong busyTicks, out ulong totalTicks)
    {
        busyTicks = 0UL;
        totalTicks = 0UL;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();

            //"cpu " with the space is the total of every processor. "cpu0", "cpu1" and so on are the
            //individual ones, and are not what is wanted here.
            if (!trimmed.StartsWith(TotalsLinePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            return TryReadTotalsLine(trimmed, out busyTicks, out totalTicks);
        }

        return false;
    }

    /// <summary>
    /// Reads the figures out of one totals line, such as
    /// <c>cpu  91234 560 18901 3023456 1310 0 452 0 0 0</c>.
    /// </summary>
    /// <param name="line">The line, beginning with <c>cpu</c>.</param>
    /// <param name="busyTicks">The time spent doing something, in clock ticks.</param>
    /// <param name="totalTicks">The time altogether, busy and idle, in clock ticks.</param>
    /// <returns>True when at least the first four figures were there.</returns>
    public static bool TryReadTotalsLine(string line, out ulong busyTicks, out ulong totalTicks)
    {
        busyTicks = 0UL;
        totalTicks = 0UL;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        //The label, then at least user, nice, system and idle.
        if (fields.Length < 5)
        {
            return false;
        }

        var user = 0UL;
        var nice = 0UL;
        var system = 0UL;
        var idle = 0UL;
        var iowait = 0UL;
        var irq = 0UL;
        var softirq = 0UL;
        var steal = 0UL;

        if (!TryReadField(fields, 1, ref user)
            || !TryReadField(fields, 2, ref nice)
            || !TryReadField(fields, 3, ref system)
            || !TryReadField(fields, 4, ref idle))
        {
            return false;
        }

        //These four are missing on older kernels, and a kernel that does not report one simply
        //contributes nothing to the figure.
        TryReadField(fields, 5, ref iowait);
        TryReadField(fields, 6, ref irq);
        TryReadField(fields, 7, ref softirq);
        TryReadField(fields, 8, ref steal);

        busyTicks = user + nice + system + irq + softirq + steal;
        totalTicks = busyTicks + idle + iowait;

        return true;
    }

    /// <summary>
    /// The percentage of the interval between two readings that the processor was busy.
    /// </summary>
    /// <param name="previousBusy">The busy total at the earlier reading.</param>
    /// <param name="previousTotal">The whole total at the earlier reading.</param>
    /// <param name="busy">The busy total at the later reading.</param>
    /// <param name="total">The whole total at the later reading.</param>
    /// <returns>A percentage between zero and a hundred.</returns>
    public static double BusyPercent(ulong previousBusy, ulong previousTotal, ulong busy, ulong total)
        => CpuBusyMath.Percent(
            CpuBusyMath.Difference(previousBusy, busy),
            CpuBusyMath.Difference(previousTotal, total));

    private static bool TryReadField(string[] fields, int index, ref ulong value)
    {
        if (index >= fields.Length)
        {
            return false;
        }

        if (!ulong.TryParse(
                fields[index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
