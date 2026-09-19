namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// One reading of the four tick counters macOS reports for the host's processors altogether. They are
/// 32 bits wide and wrap round when they fill up, so two of these are only ever compared through
/// <see cref="MacOsHostStatistics.BusyPercent" />.
/// </summary>
internal readonly struct MacOsCpuTicks
{
    public MacOsCpuTicks(uint user, uint system, uint idle, uint nice)
    {
        User = user;
        System = system;
        Idle = idle;
        Nice = nice;
    }

    /// <summary>Ticks spent in user code at ordinary priority.</summary>
    public uint User { get; }

    /// <summary>Ticks spent in the kernel.</summary>
    public uint System { get; }

    /// <summary>Ticks spent with nothing to do.</summary>
    public uint Idle { get; }

    /// <summary>Ticks spent in user code that had been asked to take second place.</summary>
    public uint Nice { get; }
}
