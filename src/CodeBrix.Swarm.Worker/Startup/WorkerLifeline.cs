using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Worker.Startup;

/// <summary>
/// Watches the pipe the Worker's configuration arrived on. The Hive writes one line and then leaves
/// the pipe open; while it is open the Hive is there. The end of it means the Hive has gone, or is
/// asking this Worker to exit, and either way the Worker winds itself down.
/// </summary>
/// <remarks>
/// Reading runs on its own long-running task, because reading a console's standard input blocks a
/// thread whatever the asynchronous method's signature suggests.
/// </remarks>
internal sealed class WorkerLifeline : IDisposable
{
    private readonly TextReader _reader;
    private readonly CancellationTokenSource _stop = new();

    private Task _watcher;
    private bool _isDisposed;

    public WorkerLifeline(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>
    /// Raised once, when the pipe ends or fails.
    /// </summary>
    public event EventHandler Closed;

    /// <summary>Starts watching.</summary>
    public void Start()
    {
        if (_watcher != null)
        {
            return;
        }

        _watcher = Task.Factory
            .StartNew(
                WatchAsync,
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();
    }

    private async Task WatchAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);

                if (line == null)
                {
                    //The end of the pipe.
                    break;
                }

                //Anything else that arrives is read and let go of: the pipe carries the
                //configuration and then nothing, and a reader that stops reading would fill it.
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            //A pipe that fails means the same thing as a pipe that ends.
        }

        if (_stop.IsCancellationRequested)
        {
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            //Already gone.
        }

        _stop.Dispose();
    }
}
