namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// The arithmetic behind a Windows processor reading, on its own so that it can be checked on any
/// operating system.
/// </summary>
/// <remarks>
/// Windows reports three running totals: time idle, time in the kernel and time in user code. THE
/// KERNEL TOTAL ALREADY INCLUDES THE IDLE TOTAL. So the length of an interval is the change in
/// kernel plus the change in user, and the busy part of it is that figure less the change in idle -
/// not kernel plus user with idle added on, which would count idle time twice and report a quiet host
/// as half busy.
/// </remarks>
internal static class WindowsCpuTimes
{
    /// <summary>
    /// The percentage of the interval between two readings that the processor was busy.
    /// </summary>
    /// <param name="previousIdle">Time idle at the earlier reading.</param>
    /// <param name="previousKernel">Time in the kernel at the earlier reading, idle included.</param>
    /// <param name="previousUser">Time in user code at the earlier reading.</param>
    /// <param name="idle">Time idle at the later reading.</param>
    /// <param name="kernel">Time in the kernel at the later reading, idle included.</param>
    /// <param name="user">Time in user code at the later reading.</param>
    /// <returns>A percentage between zero and a hundred.</returns>
    public static double BusyPercent(
        ulong previousIdle,
        ulong previousKernel,
        ulong previousUser,
        ulong idle,
        ulong kernel,
        ulong user)
    {
        var idleDelta = CpuBusyMath.Difference(previousIdle, idle);
        var totalDelta = CpuBusyMath.Difference(previousKernel, kernel)
                         + CpuBusyMath.Difference(previousUser, user);

        var busyDelta = totalDelta >= idleDelta ? totalDelta - idleDelta : 0UL;

        return CpuBusyMath.Percent(busyDelta, totalDelta);
    }
}
