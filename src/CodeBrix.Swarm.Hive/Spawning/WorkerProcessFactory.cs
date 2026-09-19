namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// Starts real Worker processes. This is what a Hive uses unless something has deliberately put a
/// substitute in its place.
/// </summary>
internal sealed class WorkerProcessFactory : IWorkerProcessFactory
{
    /// <summary>
    /// The one instance. The factory holds no state.
    /// </summary>
    public static readonly WorkerProcessFactory Instance = new();

    private WorkerProcessFactory() { }

    /// <inheritdoc />
    public IWorkerProcess Start(WorkerProcessRequest request) => WorkerProcess.Start(request);
}
