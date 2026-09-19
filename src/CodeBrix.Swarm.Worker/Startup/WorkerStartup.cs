using CodeBrix.Swarm.Core.Configuration;

namespace CodeBrix.Swarm.Worker.Startup;

/// <summary>
/// What reading a Worker's start-up configuration produced: the configuration itself, and where it
/// came from.
/// </summary>
internal sealed class WorkerStartup
{
    public WorkerStartup(WorkerConfiguration configuration, string configurationFilePath)
    {
        Configuration = configuration;
        ConfigurationFilePath = configurationFilePath;
    }

    /// <summary>The configuration, already checked for completeness.</summary>
    public WorkerConfiguration Configuration { get; }

    /// <summary>
    /// The file the configuration was read from, or null when it came from standard input.
    /// </summary>
    public string ConfigurationFilePath { get; }

    /// <summary>
    /// True when the configuration came from a file, which means the Worker was started by hand
    /// rather than by a Hive: there is no lifeline to watch.
    /// </summary>
    public bool IsDevelopmentMode => ConfigurationFilePath != null;
}
