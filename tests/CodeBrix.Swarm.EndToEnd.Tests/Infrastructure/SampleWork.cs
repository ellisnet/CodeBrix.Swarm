namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The application's own part of a Worker's configuration, as the sample Worker reads it. The swarm
/// carries this from the Hive to the Worker without reading it, so this is the piece these scenarios
/// check arrives intact.
/// </summary>
internal sealed class SampleWork
{
    /// <summary>Something to call this piece of work in what the Worker writes.</summary>
    public string Label { get; set; } = "work";

    /// <summary>How many steps to take, or zero to carry on until the Worker is ended.</summary>
    public int Steps { get; set; }

    /// <summary>How long to wait between steps.</summary>
    public int StepMilliseconds { get; set; } = 100;

    /// <summary>Which step to fail on, counting from zero, or below zero never to fail.</summary>
    public int FailAtStep { get; set; } = -1;

    /// <summary>True to write a line for everything that happens.</summary>
    public bool Report { get; set; }
}
