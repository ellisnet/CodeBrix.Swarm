namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// The arithmetic behind a macOS reading, on its own so that it can be checked on any operating
/// system. What the kernel hands over is a set of page counts and a set of tick counters; turning
/// those into "how much memory could be handed out" and "how busy was the processor" is all here.
/// </summary>
internal static class MacOsHostStatistics
{
    /// <summary>
    /// How much memory could be handed to something new, worked out from the kernel's page counts.
    /// </summary>
    /// <param name="totalBytes">The host's physical memory, as the system reports it.</param>
    /// <param name="pageSizeBytes">How many bytes one page is.</param>
    /// <param name="internalPages">Pages that belong to a process and are not backed by a file.</param>
    /// <param name="purgeablePages">
    /// Pages a process has marked as ones the kernel may simply throw away. They are counted inside
    /// the internal pages, and they are available, so they come off again.
    /// </param>
    /// <param name="wiredPages">Pages that cannot be paged out at all.</param>
    /// <param name="compressorPages">Pages the memory compressor itself is holding.</param>
    /// <returns>
    /// How much memory is available, in bytes, never below zero and never above the total.
    /// </returns>
    /// <remarks>
    /// <para>
    /// macOS has no single figure to read, the way Linux publishes one and Windows answers one. What
    /// it reports is the same set of counts its own memory display is built from, and the figure that
    /// display calls "memory used" is the pages belonging to processes, plus the wired pages, plus
    /// whatever the compressor holds, less the pages a process has said may be thrown away. What is
    /// left of the host's memory is what could be handed out.
    /// </para>
    /// <para>
    /// File-backed pages are deliberately NOT counted as used. They are the same thing as the Linux
    /// page cache: a host keeps as many of them as it can and gives them back the moment something
    /// asks, so counting them would make every working host look full.
    /// </para>
    /// </remarks>
    public static long AvailableBytes(
        long totalBytes,
        long pageSizeBytes,
        uint internalPages,
        uint purgeablePages,
        uint wiredPages,
        uint compressorPages)
    {
        if (totalBytes <= 0L || pageSizeBytes <= 0L)
        {
            return 0L;
        }

        //Widened before the subtraction: page counts are unsigned, and purgeable pages could in
        //principle be reported as more than the internal ones they are counted inside.
        var usedPages = (long)internalPages - purgeablePages + wiredPages + compressorPages;

        if (usedPages < 0L)
        {
            usedPages = 0L;
        }

        var usedBytes = usedPages * pageSizeBytes;

        if (usedBytes < 0L || usedBytes > totalBytes)
        {
            usedBytes = usedBytes < 0L ? 0L : totalBytes;
        }

        return totalBytes - usedBytes;
    }

    /// <summary>
    /// The percentage of the interval between two readings that the processor was busy.
    /// </summary>
    /// <param name="previous">The tick counters at the earlier reading.</param>
    /// <param name="current">The tick counters at the later reading.</param>
    /// <returns>A percentage between zero and a hundred.</returns>
    /// <remarks>
    /// The counters are 32 bits wide and wrap round when they fill up, which on a busy host with many
    /// processors happens in a matter of weeks. Differences are taken the way that counts the wrap,
    /// so a reading that straddles one is right rather than nonsense.
    /// </remarks>
    public static double BusyPercent(MacOsCpuTicks previous, MacOsCpuTicks current)
    {
        var user = CpuBusyMath.WrappingDifference(previous.User, current.User);
        var system = CpuBusyMath.WrappingDifference(previous.System, current.System);
        var nice = CpuBusyMath.WrappingDifference(previous.Nice, current.Nice);
        var idle = CpuBusyMath.WrappingDifference(previous.Idle, current.Idle);

        var busy = user + system + nice;

        return CpuBusyMath.Percent(busy, busy + idle);
    }
}
