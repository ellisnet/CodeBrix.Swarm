namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// Starts Worker processes. The real implementation starts real ones; a test substitutes one that
/// hands back something it can end on demand.
/// </summary>
internal interface IWorkerProcessFactory
{
    /// <summary>
    /// Starts one Worker and hands it its configuration.
    /// </summary>
    /// <param name="request">What to start, and what to tell it.</param>
    /// <returns>The running Worker.</returns>
    IWorkerProcess Start(WorkerProcessRequest request);
}
