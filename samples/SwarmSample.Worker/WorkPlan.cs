namespace SwarmSample.Worker;

/// <summary>
/// The part of this Worker's configuration that belongs to the application rather than to the swarm.
/// A Hive puts one of these in every Worker it starts, and the swarm carries it here without reading
/// it: as far as CodeBrix.Swarm is concerned it is an opaque piece of JSON.
/// </summary>
/// <remarks>
/// The work itself is deliberately dull - count, wait, say so - because the point of the sample is the
/// shape of a Worker, not what one does. Everything a real Worker would do goes exactly where the
/// counting is.
/// </remarks>
internal sealed class WorkPlan
{
    /// <summary>Something to call this piece of work in what the Worker writes.</summary>
    public string Label { get; set; } = "work";

    /// <summary>
    /// How many steps to take before the work is finished, or zero to carry on until something ends
    /// the Worker.
    /// </summary>
    public int Steps { get; set; }

    /// <summary>How long to wait between steps.</summary>
    public int StepMilliseconds { get; set; } = 250;

    /// <summary>
    /// Which step to fail on, counting from zero, or below zero never to fail. Failing on step zero is
    /// a Worker that cannot do its work at all, which is what a Hive waits longer after.
    /// </summary>
    public int FailAtStep { get; set; } = -1;

    /// <summary>
    /// True to write a line for everything that happens. It is off by default so that a swarm of these
    /// says nothing at all unless somebody asked it to.
    /// </summary>
    public bool Report { get; set; }
}
