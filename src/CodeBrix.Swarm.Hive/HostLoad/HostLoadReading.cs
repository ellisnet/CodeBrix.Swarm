namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// One measurement of how busy the host is: how much memory it has and how much of that is really
/// available, and how busy its processor has been since the reading before this one.
/// </summary>
/// <remarks>
/// A reading whose total is zero means the measurement did not work. A Hive that cannot measure the
/// host does not start Workers on it - it says so instead of guessing.
/// </remarks>
internal sealed class HostLoadReading
{
    /// <summary>A reading that says nothing, for when the measurement did not work.</summary>
    public static readonly HostLoadReading Unknown = new(0L, 0L, 0d);

    public HostLoadReading(long totalRamBytes, long availableRamBytes, double cpuBusyPercent)
    {
        TotalRamBytes = totalRamBytes;
        AvailableRamBytes = availableRamBytes;
        CpuBusyPercent = cpuBusyPercent;
    }

    /// <summary>How much memory the host has altogether, in bytes.</summary>
    public long TotalRamBytes { get; }

    /// <summary>
    /// How much memory could be given to something new without pushing the host into swapping, in
    /// bytes. This is not the same as memory that is unused: a healthy host uses nearly all of its
    /// memory, much of it for caches it will give up the moment something asks.
    /// </summary>
    public long AvailableRamBytes { get; }

    /// <summary>
    /// How busy the processor was, as a percentage, over the interval between this reading and the
    /// one before it. It is zero for the first reading, because there is nothing to compare with yet.
    /// </summary>
    public double CpuBusyPercent { get; }

    /// <summary>True when the measurement worked and the numbers mean something.</summary>
    public bool IsUsable => TotalRamBytes > 0L && AvailableRamBytes >= 0L;

    /// <summary>
    /// How much of the host's memory is in use, as a percentage. Zero when the measurement did not
    /// work.
    /// </summary>
    public double UsedRamPercent => TotalRamBytes <= 0L
        ? 0d
        : (TotalRamBytes - AvailableRamBytes) * 100d / TotalRamBytes;
}
