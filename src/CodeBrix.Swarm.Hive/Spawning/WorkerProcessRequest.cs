using System;
using System.Collections.Generic;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// Everything needed to start one Worker: what to start, what to start it with, and the one line of
/// JSON to hand it over its standard input the moment it is running.
/// </summary>
internal sealed class WorkerProcessRequest
{
    public WorkerProcessRequest(
        string workerId,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string configurationJsonLine,
        bool captureOutput,
        Action<WorkerOutputLine> output)
    {
        WorkerId = workerId;
        Executable = executable;
        Arguments = arguments ?? [];
        WorkingDirectory = workingDirectory;
        ConfigurationJsonLine = configurationJsonLine;
        CaptureOutput = captureOutput;
        Output = output;
    }

    /// <summary>What this Worker is to be called.</summary>
    public string WorkerId { get; }

    /// <summary>The program to start.</summary>
    public string Executable { get; }

    /// <summary>The arguments, each one handed over separately so nothing has to be quoted.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The folder to start it in, or null for the Hive's own.</summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// The configuration, as the single line of JSON the Worker reads first. The pipe it goes down
    /// then stays open as the lifeline.
    /// </summary>
    public string ConfigurationJsonLine { get; }

    /// <summary>True to read what the Worker writes rather than letting it go where the Hive's goes.</summary>
    public bool CaptureOutput { get; }

    /// <summary>Where each collected line goes. May be null even when collecting is on.</summary>
    public Action<WorkerOutputLine> Output { get; }
}
