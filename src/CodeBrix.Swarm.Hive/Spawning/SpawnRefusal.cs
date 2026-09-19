namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// Why a Hive is not starting another Worker at this moment. None of these is a failure: every one of
/// them is a reason to wait and measure the host again.
/// </summary>
internal enum SpawnRefusal
{
    /// <summary>There is room. Ask the application what to start.</summary>
    None = 0,

    /// <summary>
    /// The host could not be measured at all, so there is no telling whether there is room. A Hive
    /// that cannot measure its host does not start Workers on it.
    /// </summary>
    HostLoadUnknown = 1,

    /// <summary>
    /// This Hive already has as many Workers running as the application said it may.
    /// </summary>
    AtWorkerLimit = 2,

    /// <summary>
    /// Another Worker the size of the ones already running would push the host's memory past the
    /// share the Hive is allowed.
    /// </summary>
    RamPercentReached = 3,

    /// <summary>
    /// Another Worker the size of the ones already running would leave the host with less free memory
    /// than the floor allows, whatever the percentage says.
    /// </summary>
    FreeRamFloorReached = 4,

    /// <summary>
    /// The host's processor has been busier than the share the Hive is allowed, averaged over the
    /// window.
    /// </summary>
    CpuPercentReached = 5
}
