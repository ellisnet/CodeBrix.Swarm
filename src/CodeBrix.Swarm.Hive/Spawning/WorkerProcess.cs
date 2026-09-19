using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// One real Worker process: started, handed its configuration down a pipe that then stays open as its
/// lifeline, watched, and - when the time comes - closed down politely and then not politely.
/// </summary>
/// <remarks>
/// <para>
/// A REDIRECTED STREAM THAT NOBODY READS FILLS UP, AND THE PROCESS WRITING TO IT THEN STOPS RUNNING
/// UNTIL SOMEBODY EMPTIES IT. That is the one thing to get right about starting processes and reading
/// what they write, and it is why collecting a Worker's output is off unless it is asked for, and why
/// the streams are read continuously from the moment the process starts whenever it is on. A Worker
/// whose output nobody was reading would simply stop, some minutes in, with no sign of why.
/// </para>
/// <para>
/// Standard input is a different matter and is always redirected: that pipe is how the configuration
/// gets to the Worker in the first place, and its staying open afterwards is what tells the Worker its
/// Hive is still there.
/// </para>
/// </remarks>
internal sealed class WorkerProcess : IWorkerProcess
{
    private readonly Action<WorkerOutputLine> _output;
    private readonly bool _captureOutput;

    private Process _process;
    private bool _isLifelineClosed;
    private bool _isDisposed;
    private int _cachedExitCode;
    private bool _haveCachedExitCode;

    private WorkerProcess(string workerId, Process process, bool captureOutput, Action<WorkerOutputLine> output)
    {
        WorkerId = workerId;
        _process = process;
        _captureOutput = captureOutput;
        _output = output;
        ProcessId = process.Id;
        StartedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Starts a Worker and hands it its configuration.
    /// </summary>
    /// <param name="request">What to start, and what to tell it.</param>
    /// <returns>The running Worker.</returns>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    public static WorkerProcess Start(WorkerProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            //Always: this is the pipe the configuration travels down, and its staying open afterwards
            //is the lifeline.
            RedirectStandardInput = true,
            RedirectStandardOutput = request.CaptureOutput,
            RedirectStandardError = request.CaptureOutput,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        //Each argument separately, so that a path with a space in it needs no quoting and no quoting
        //rule has to be guessed at.
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };

        var worker = default(WorkerProcess);

        try
        {
            process.Start();

            worker = new WorkerProcess(request.WorkerId, process, request.CaptureOutput, request.Output);

            if (request.CaptureOutput)
            {
                //Reading starts before anything else, because everything else takes time and the
                //Worker is already writing.
                process.OutputDataReceived += worker.OnOutputLine;
                process.ErrorDataReceived += worker.OnErrorLine;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            worker.LowerPriority();

            //Linux only, best effort: if this host ever runs out of memory altogether, lose a Worker
            //rather than the Hive or whatever else the host is for.
            LinuxOomPreference.TryPrefer(process.Id);

            worker.WriteConfiguration(request.ConfigurationJsonLine);

            return worker;
        }
        catch (Exception)
        {
            if (worker != null)
            {
                worker.Dispose();
            }
            else
            {
                process.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public string WorkerId { get; }

    /// <inheritdoc />
    public int ProcessId { get; }

    /// <inheritdoc />
    public DateTime StartedUtc { get; }

    /// <inheritdoc />
    public bool HasExited
    {
        get
        {
            var process = _process;

            if (process == null)
            {
                return true;
            }

            try
            {
                return process.HasExited;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <inheritdoc />
    public int ExitCode
    {
        get
        {
            if (_haveCachedExitCode)
            {
                return _cachedExitCode;
            }

            var process = _process;

            if (process == null)
            {
                return _cachedExitCode;
            }

            try
            {
                if (!process.HasExited)
                {
                    return 0;
                }

                _cachedExitCode = process.ExitCode;
                _haveCachedExitCode = true;
                return _cachedExitCode;
            }
            catch (Exception)
            {
                return _cachedExitCode;
            }
        }
    }

    /// <inheritdoc />
    public long WorkingSetBytes
    {
        get
        {
            var process = _process;

            if (process == null)
            {
                return 0L;
            }

            try
            {
                if (process.HasExited)
                {
                    return 0L;
                }

                //Without this the figure is whatever it was when the process object last looked.
                process.Refresh();
                return process.WorkingSet64;
            }
            catch (Exception)
            {
                return 0L;
            }
        }
    }

    /// <inheritdoc />
    public void CloseLifeline()
    {
        if (_isLifelineClosed)
        {
            return;
        }

        _isLifelineClosed = true;

        var process = _process;

        if (process == null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception)
        {
            //The Worker may have closed its end, or ended altogether. Either way the lifeline is no
            //longer open, which is the whole point of closing it.
        }
    }

    /// <inheritdoc />
    public void ForceStop()
    {
        var process = _process;

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                //Everything it started as well: a Worker that started something of its own must not
                //leave it running on the host.
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            //It ended between the question and the answer, or the host will not let this process be
            //stopped. Nothing further can be done about either from here.
        }
    }

    /// <inheritdoc />
    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        var process = _process;

        if (process == null)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            //A process object that can no longer be asked is a process that has ended.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        var process = _process;
        _process = null;

        if (process == null)
        {
            return;
        }

        if (_captureOutput)
        {
            process.OutputDataReceived -= OnOutputLine;
            process.ErrorDataReceived -= OnErrorLine;
        }

        //Read the code while the process object still can, so that a report written after this can
        //still say what the Worker ended with.
        try
        {
            if (process.HasExited && !_haveCachedExitCode)
            {
                _cachedExitCode = process.ExitCode;
                _haveCachedExitCode = true;
            }
        }
        catch (Exception)
        {
            //Nothing more to learn about it.
        }

        try
        {
            process.Dispose();
        }
        catch (Exception)
        {
            //Already gone.
        }
    }

    private void WriteConfiguration(string jsonLine)
    {
        var process = _process;

        if (process == null)
        {
            return;
        }

        //One line, then a flush, and the writer is deliberately NOT closed: the open pipe is the
        //lifeline, and closing it here would tell the Worker to exit before it had started.
        process.StandardInput.WriteLine(jsonLine);
        process.StandardInput.Flush();
    }

    private void LowerPriority()
    {
        var process = _process;

        if (process == null)
        {
            return;
        }

        try
        {
            //Below the ordinary, so that a host with as many Workers on it as it can hold still
            //answers its Hive, its coordinator and whoever is logged in to it.
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception)
        {
            //Some hosts do not allow it, and one that does not is no reason to refuse the Worker.
        }
    }

    private void OnOutputLine(object sender, DataReceivedEventArgs e) => Deliver(false, e?.Data);

    private void OnErrorLine(object sender, DataReceivedEventArgs e) => Deliver(true, e?.Data);

    private void Deliver(bool isError, string text)
    {
        //A null line is how the stream says it has ended. The line is read either way - reading is
        //what keeps the Worker from filling the pipe up - and only handed on when somebody asked for
        //it.
        if (text == null)
        {
            return;
        }

        var output = _output;

        if (output == null)
        {
            return;
        }

        try
        {
            output(new WorkerOutputLine(WorkerId, ProcessId, isError, text));
        }
        catch (Exception)
        {
            //Passing a line on is best effort; something that throws while being told about a line
            //must not end the Worker.
        }
    }
}
