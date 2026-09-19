using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Looks for Worker processes that are still running, anywhere on this machine. EVERY scenario in this
/// suite ends by asserting that none is, because a suite that starts real processes and does not check
/// that they went is a suite that quietly fills a machine up.
/// </summary>
/// <remarks>
/// The search is by what a process was started as rather than by its name, because a process name on
/// Linux is cut short at fifteen characters and the sample Worker's is longer than that. Asking the
/// operating system's own record of every process is also the only honest check: a Hive that has let go
/// of a Worker cannot be asked whether it went.
/// </remarks>
internal static class SpawnedProcesses
{
    private const string SampleWorkerName = "SwarmSample.Worker";

    /// <summary>
    /// Every process on this machine that was started as the sample Worker.
    /// </summary>
    /// <returns>Their process numbers.</returns>
    public static IReadOnlyList<int> FindSampleWorkers()
    {
        return OperatingSystem.IsLinux()
            ? FindByCommandLine()
            : FindByProcessName();
    }

    /// <summary>
    /// Waits until no sample Worker is running any more, or the deadline passes.
    /// </summary>
    /// <param name="within">How long to allow. A process that has been asked to end takes a moment.</param>
    /// <param name="cancellationToken">Cancelled to stop waiting.</param>
    /// <returns>Whatever was still running when the wait ended.</returns>
    public static async Task<IReadOnlyList<int>> WaitUntilNoneAreLeftAsync(
        TimeSpan within,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + within;
        var left = FindSampleWorkers();

        while (left.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            left = FindSampleWorkers();
        }

        return left;
    }

    /// <summary>
    /// Stops anything that is left, whatever it takes. This is the safety net at the end of a scenario
    /// that went wrong: the assertion has already failed by then, and a failed test must still not leave
    /// processes on the machine.
    /// </summary>
    /// <returns>How many had to be stopped.</returns>
    public static int StopAnythingLeft()
    {
        var stopped = 0;

        foreach (var processId in FindSampleWorkers())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                stopped++;
            }
            catch (Exception)
            {
                //It went between being found and being stopped, or it is not ours to stop.
            }
        }

        return stopped;
    }

    private static IReadOnlyList<int> FindByCommandLine()
    {
        var found = new List<int>();

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            var name = System.IO.Path.GetFileName(directory);

            if (!int.TryParse(name, out var processId))
            {
                continue;
            }

            string commandLine;

            try
            {
                commandLine = File.ReadAllText(System.IO.Path.Combine(directory, "cmdline"));
            }
            catch (Exception)
            {
                //The process ended while its own record was being read, or it is not ours to read.
                continue;
            }

            if (commandLine.Contains(SampleWorkerName, StringComparison.Ordinal))
            {
                found.Add(processId);
            }
        }

        return found;
    }

    private static IReadOnlyList<int> FindByProcessName()
    {
        var found = new List<int>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.Contains("SwarmSample", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(process.Id);
                }
            }
            catch (Exception)
            {
                //A process that cannot be asked its name is not one of ours.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }
}
