using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Configuration;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Starts a real Worker process with no Hive anywhere: the configuration goes in a file, the file is named
/// with the library's <c>--swarm-config</c> switch, and the Worker runs on its own. This is the way a
/// Worker is debugged, and it has to keep working, so a scenario exercises it.
/// </summary>
/// <remarks>
/// Both of the Worker's output streams are redirected here, and BOTH ARE READ CONTINUOUSLY. A redirected
/// stream that nobody reads fills up, and the process writing to it then stops running until somebody
/// empties it - which would look exactly like a Worker that had hung.
/// </remarks>
internal static class WorkerByHand
{
    /// <summary>
    /// Starts a Worker from a configuration file, waits for it to end, and hands back what it exited with
    /// and everything it wrote.
    /// </summary>
    /// <param name="configuration">What to put in the file.</param>
    /// <param name="arguments">
    /// Anything else to start it with. The <c>--swarm-config</c> switch and the file's path are added
    /// after these.
    /// </param>
    /// <param name="cancellationToken">Cancelled to give up waiting and stop the process.</param>
    /// <returns>The exit code and the lines it wrote.</returns>
    public static async Task<WorkerByHandResult> RunAsync(
        WorkerConfiguration configuration,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "swarm-worker-" + Guid.NewGuid().ToString("N") + ".json");

        await File.WriteAllTextAsync(path, configuration.ToJsonLine(), cancellationToken)
            .ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = SampleWorkerProgram.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        //THE ONLY WAY INTO DEVELOPMENT MODE. Nothing else on this command line can put the Worker there
        //- not a positional argument, and not an application's own --config.
        startInfo.ArgumentList.Add("--swarm-config");
        startInfo.ArgumentList.Add(path);

        var lines = new List<string>();
        var gate = new object();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, line) => Collect(gate, lines, line.Data);
        process.ErrorDataReceived += (_, line) => Collect(gate, lines, line.Data);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            lock (gate)
            {
                return new WorkerByHandResult(process.ExitCode, lines.ToArray());
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                //It ended between the question and the answer.
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                //A temporary file that will not go is nothing to fail a scenario over.
            }
        }
    }

    /// <summary>
    /// Starts a Worker from a configuration file and hands back only what it exited with.
    /// </summary>
    /// <param name="configuration">What to put in the file.</param>
    /// <param name="arguments">Anything else to start it with.</param>
    /// <param name="cancellationToken">Cancelled to give up waiting and stop the process.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunToCompletionAsync(
        WorkerConfiguration configuration,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(configuration, arguments, cancellationToken).ConfigureAwait(false);

        return result.ExitCode;
    }

    private static void Collect(object gate, List<string> lines, string text)
    {
        if (text == null)
        {
            return;
        }

        lock (gate)
        {
            lines.Add(text);
        }
    }
}
