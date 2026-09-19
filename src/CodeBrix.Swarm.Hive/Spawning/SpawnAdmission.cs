using CodeBrix.Swarm.Hive.HostLoad;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// The one decision at the centre of a Hive: given what the host looks like right now and what the
/// Workers already running are costing, is there room for one more?
/// </summary>
/// <remarks>
/// <para>
/// NEVER OVERSUBSCRIBING A HOST IS NOT A MATTER OF READING A NUMBER ONCE. A Worker that has just
/// started has not yet taken up the memory it is going to, so a Hive that started Workers while the
/// host still read as quiet would start far too many of them before the first reading caught up. Two
/// things together keep that from happening: Workers are started one at a time with a settle interval
/// between them, and the decision is made against the host's memory AS IT WOULD BE once another
/// Worker the size of the ones already running had taken its share.
/// </para>
/// <para>
/// The first Worker is therefore admitted on the host's own figures - there is nothing yet to learn a
/// footprint from - and every one after it has to fit alongside what its predecessors turned out to
/// need.
/// </para>
/// <para>
/// Every one of these decisions is about starting something NEW. Nothing here ever ends a Worker that
/// is already running.
/// </para>
/// </remarks>
internal static class SpawnAdmission
{
    /// <summary>
    /// Decides whether there is room for another Worker.
    /// </summary>
    /// <param name="reading">The latest measurement of the host.</param>
    /// <param name="averageCpuPercent">
    /// How busy the processor has been, averaged over the limit's window.
    /// </param>
    /// <param name="runningWorkers">How many Workers this Hive has running.</param>
    /// <param name="averageWorkerBytes">
    /// What a Worker of this Hive's has turned out to cost, in bytes, or zero when none is running
    /// yet.
    /// </param>
    /// <param name="limits">How much of the host this Hive is allowed.</param>
    /// <returns>
    /// <see cref="SpawnRefusal.None" /> when there is room, or the reason there is not.
    /// </returns>
    public static SpawnRefusal Evaluate(
        HostLoadReading reading,
        double averageCpuPercent,
        int runningWorkers,
        long averageWorkerBytes,
        SwarmHostLimits limits)
    {
        if (limits == null)
        {
            return SpawnRefusal.HostLoadUnknown;
        }

        if (limits.MaxWorkers.HasValue && runningWorkers >= limits.MaxWorkers.Value)
        {
            return SpawnRefusal.AtWorkerLimit;
        }

        if (reading == null || !reading.IsUsable)
        {
            return SpawnRefusal.HostLoadUnknown;
        }

        var footprint = averageWorkerBytes > 0L ? averageWorkerBytes : 0L;
        var projectedAvailable = reading.AvailableRamBytes - footprint;

        if (projectedAvailable < 0L)
        {
            projectedAvailable = 0L;
        }

        if (projectedAvailable < limits.FreeRamFloorBytes)
        {
            return SpawnRefusal.FreeRamFloorReached;
        }

        var projectedUsedPercent =
            (reading.TotalRamBytes - projectedAvailable) * 100d / reading.TotalRamBytes;

        if (projectedUsedPercent > limits.MaxRamPercent)
        {
            return SpawnRefusal.RamPercentReached;
        }

        if (averageCpuPercent > limits.MaxCpuPercent)
        {
            return SpawnRefusal.CpuPercentReached;
        }

        return SpawnRefusal.None;
    }
}
