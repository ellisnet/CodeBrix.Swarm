using System;
using CodeBrix.Swarm.Core.Configuration;
using CodeBrix.Swarm.Hive.Spawning;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// One Worker a Hive started during a scenario: what it was told, and the process number it was given.
/// </summary>
internal sealed class StartedWorker
{
    public StartedWorker(WorkerProcessRequest request, int processId)
    {
        Request = request;
        ProcessId = processId;
        StartedUtc = DateTime.UtcNow;
        Configuration = WorkerConfiguration.Parse(request.ConfigurationJsonLine);
    }

    /// <summary>Everything the Hive asked for when it started this Worker.</summary>
    public WorkerProcessRequest Request { get; }

    /// <summary>The operating system's number for the process.</summary>
    public int ProcessId { get; }

    /// <summary>
    /// When this Worker was started, which is how a scenario sees the pacing between one Worker and the
    /// next - and therefore whether a wait after a failure came into it.
    /// </summary>
    public DateTime StartedUtc { get; }

    /// <summary>
    /// The configuration as it was written to the Worker's standard input, read back the way the Worker
    /// itself reads it.
    /// </summary>
    public WorkerConfiguration Configuration { get; }

    /// <summary>What the Hive called this Worker.</summary>
    public string WorkerId => Request.WorkerId;
}
